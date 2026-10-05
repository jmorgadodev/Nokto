using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nokto.Core.Services;

namespace Nokto.ConsoleTest;

internal static class QuotaRegressionTests
{
    // Fixed reset dates deliberately avoid clock-dependent fixtures and boundary races.
    private const long FutureReset = 4102444800; // 2100-01-01 UTC
    private const long ExpiredReset = 946684800; // 2000-01-01 UTC

    public static void Run()
    {
        Test("Codex SQLite: nested usage percentages", f =>
        {
            f.WriteDatabase("Codex", "openai.chatgpt", """
                {"cache":{"quota":{"windowUsagePercent":6,"weeklyUsagePercent":1}}}
                """);
            AssertUsage(f.Environment("codex"), 6, 1);
        });

        Test("Codex SQLite: escaped JSON usage percentages", f =>
        {
            string inner = """{"windowUsagePercent":26.5,"weeklyUsagePercent":71.25}""";
            f.WriteDatabase("Codex", "openai.chatgpt", "{\"state\":\"" + JsonEncodedText.Encode(inner) + "\"}");
            AssertUsage(f.Environment("codex"), 26.5, 71.25);
        });

        Test("Codex SQLite: explicitly named remaining percentages", f =>
        {
            f.WriteDatabase("Codex", "openai.chatgpt.quota", """
                {"fiveHourRemainingPercent":94,"weeklyRemainingPercent":99}
                """);
            AssertUsage(f.Environment("codex"), 6, 1);
        });

        Test("Codex: generic percentages cannot establish window quotas", f =>
        {
            f.WriteDatabase("Codex", "openai.chatgpt.quota", """
                {"percentage":99,"remainingPercent":55,"windowPercentageLeft":94,"quotaPercent":6}
                """);
            AssertUnknown(f.Environment("codex"));
        });

        Test("Codex JSONL: token_count primary and secondary windows", f =>
        {
            f.WriteSession(Event(6, 1));
            AssertUsage(f.Environment("codex"), 6, 1);
        });

        Test("Codex JSONL: classify windows by duration instead of position", f =>
        {
            f.WriteSession(Event(12, 65, primaryMinutes: 10080, secondaryMinutes: 300));
            AssertUsage(f.Environment("codex"), 65, 12);
        });

        Test("Codex JSONL: latest valid event survives a malformed trailing line", f =>
        {
            f.WriteSession(Event(5, 10, timestamp: "2026-10-01T12:00:00Z"),
                Event(6, 1, timestamp: "2026-10-01T13:00:00Z"), "{\"type\":\"event_msg\",broken");
            AssertUsage(f.Environment("codex"), 6, 1);
        });

        Test("Codex JSONL: expired windows provide no current quota", f =>
        {
            f.WriteSession(Event(90, 95, reset: ExpiredReset));
            AssertUnknown(f.Environment("codex"));
        });

        Test("Codex JSONL: expiration is evaluated independently for each window", f =>
        {
            f.WriteSession(Event(90, 12, primaryReset: ExpiredReset));
            var quota = f.Environment("codex");
            Equal(null, quota.FiveHourUsagePercentage, "Expired five-hour quota");
            Equal(12, quota.WeeklyUsagePercentage, "Current weekly quota");
            Require(quota.HasExplicitQuotaMetrics, "A valid weekly quota remains explicit.");
        });

        Test("Codex JSONL: unrelated limit_id cannot overwrite Codex quotas", f =>
        {
            f.WriteSession(Event(6, 1, timestamp: "2026-10-01T12:00:00Z"),
                Event(99, 98, limitId: "codex_other_model", timestamp: "2026-10-01T13:00:00Z"));
            AssertUsage(f.Environment("codex"), 6, 1);
        });

        Test("Codex JSONL: unsupported duration and unrelated event are ignored", f =>
        {
            f.WriteSession(Event(6, 1, primaryMinutes: 60, secondaryMinutes: 1440),
                """{"type":"response_item","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":80,"window_minutes":300,"resets_at":4102444800}}}}""");
            AssertUnknown(f.Environment("codex"));
        });

        Test("Codex JSONL: quota JSON embedded in user prompts is not telemetry", f =>
        {
            string prompt = "Use this example: " + Event(80, 90);
            f.WriteSession("{\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\""
                + JsonEncodedText.Encode(prompt) + "\"}}");
            AssertUnknown(f.Environment("codex"));
        });

        Test("Codex: invalid percentages and malformed JSON never become quotas", f =>
        {
            f.WriteDatabase("Codex", "openai.chatgpt.quota", """
                {"windowUsagePercent":-1,"weeklyUsagePercent":101}
                """);
            f.AddDatabaseRow("Codex", "openai.chatgpt.cache", """
                {"windowUsagePercent":"NaN","weeklyUsagePercent":1e400}
                """);
            f.AddDatabaseRow("Codex", "openai.chatgpt.damaged", "{\"windowUsagePercent\":6,");
            f.WriteSession(Event(-1, 101), "{not-json}");
            AssertUnknown(f.Environment("codex"));
        });

        Test("Codex: zero and one hundred are valid boundaries", f =>
        {
            f.WriteSession(Event(0, 100));
            AssertUsage(f.Environment("codex"), 0, 100);
        });

        Test("VS Code: Copilot and Antigravity state cannot imply Codex quota", f =>
        {
            // Detect Codex independently so the test checks provenance rather than detection.
            Directory.CreateDirectory(Path.Combine(f.UserProfile, ".codex"));
            f.WriteDatabase("Code", "github.copilot.quota", """{"windowUsagePercent":77,"weeklyUsagePercent":88}""");
            f.AddDatabaseRow("Code", "antigravity.quota", """{"windowUsagePercent":66,"weeklyUsagePercent":55}""");
            AssertUnknown(f.Environment("codex"));
        });

        Test("VS Code: OpenAI state supplies Codex quota among unrelated extensions", f =>
        {
            f.WriteDatabase("Code", "github.copilot.quota", """{"windowUsagePercent":77,"weeklyUsagePercent":88}""");
            f.AddDatabaseRow("Code", "openai.chatgpt", """{"windowUsagePercent":6,"weeklyUsagePercent":1}""");
            AssertUsage(f.Environment("codex"), 6, 1);
        });

        Test("Antigravity IDE: complete SQLite value beyond 500 characters", f =>
        {
            string json = "{\"padding\":\"" + new string('x', 800) + "\",\"fiveHourRemainingPercent\":94,\"weeklyRemainingPercent\":99}";
            f.WriteDatabase("Antigravity IDE", "antigravity.quota", json);
            var quota = f.Environment("antigravity");
            AssertUsage(quota, 6, 1);
        });

        Test("Antigravity: nested JSON session supplies explicit window usage", f =>
        {
            string directory = Path.Combine(f.UserProfile, ".antigravity");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "session.json"), """
                {"session":{"quota":{"windowUsagePercent":26,"weeklyUsagePercent":13}}}
                """);
            AssertUsage(f.Environment("antigravity"), 26, 13);
        });

        Test("SQLite: committed quota still in WAL is visible", f =>
        {
            using var writer = f.OpenDatabase("Codex");
            Execute(writer, "PRAGMA journal_mode=WAL;");
            Execute(writer, "PRAGMA wal_autocheckpoint=0;");
            Execute(writer, "PRAGMA wal_checkpoint(TRUNCATE);");
            Insert(writer, "openai.chatgpt", """{"windowUsagePercent":6,"weeklyUsagePercent":1}""");
            string wal = f.DatabasePath("Codex") + "-wal";
            Require(File.Exists(wal) && new FileInfo(wal).Length > 0, "Fixture must retain committed quota in WAL while the writer remains open.");
            AssertUsage(f.Environment("codex"), 6, 1);
        });
    }

    private static string Event(double primary, double secondary, long reset = FutureReset,
        string limitId = "codex", string timestamp = "2026-10-01T12:00:00Z",
        int primaryMinutes = 300, int secondaryMinutes = 10080, long? primaryReset = null)
    {
        return "{\"timestamp\":\"" + JsonEncodedText.Encode(timestamp)
            + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\""
            + JsonEncodedText.Encode(limitId) + "\",\"primary\":{\"used_percent\":"
            + primary.ToString(CultureInfo.InvariantCulture) + ",\"window_minutes\":"
            + primaryMinutes.ToString(CultureInfo.InvariantCulture) + ",\"resets_at\":"
            + (primaryReset ?? reset).ToString(CultureInfo.InvariantCulture)
            + "},\"secondary\":{\"used_percent\":" + secondary.ToString(CultureInfo.InvariantCulture)
            + ",\"window_minutes\":" + secondaryMinutes.ToString(CultureInfo.InvariantCulture)
            + ",\"resets_at\":" + reset.ToString(CultureInfo.InvariantCulture) + "}}}}";
    }

    private static void Test(string name, Action<Fixture> test)
    {
        using var fixture = new Fixture();
        try
        {
            test(fixture);
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Quota regression failed: {name}. {ex.Message}", ex);
        }
    }

    private static void AssertUsage(AiEnvironmentQuota quota, double fiveHour, double weekly)
    {
        Equal(fiveHour, quota.FiveHourUsagePercentage, "Five-hour usage");
        Equal(weekly, quota.WeeklyUsagePercentage, "Weekly usage");
        Require(quota.HasExplicitQuotaMetrics, "Real parsed quotas must be marked explicit.");
        Require(quota.Metrics.Any(m => Math.Abs(m.RemainingPercent - (100 - fiveHour)) < 0.00001),
            "Five-hour remaining metric must be the complement of usage.");
        Require(quota.Metrics.Any(m => Math.Abs(m.RemainingPercent - (100 - weekly)) < 0.00001),
            "Weekly remaining metric must be the complement of usage.");
    }

    private static void AssertUnknown(AiEnvironmentQuota quota)
    {
        Equal(null, quota.FiveHourUsagePercentage, "Unknown five-hour usage");
        Equal(null, quota.WeeklyUsagePercentage, "Unknown weekly usage");
        Require(!quota.HasExplicitQuotaMetrics, "Unsupported, expired or malformed evidence must not establish a quota.");
        Require(quota.Metrics.Count == 0, "Unknown quota must not include invented metrics.");
    }

    private static void Equal(double? expected, double? actual, string description)
    {
        bool equal = expected.HasValue
            ? actual.HasValue && double.IsFinite(actual.Value) && Math.Abs(expected.Value - actual.Value) < 0.00001
            : !actual.HasValue;
        Require(equal, $"{description}: expected {expected?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, received {actual?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO ItemTable (key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"nokto_quota_regression_{Guid.NewGuid():N}");
        public string UserProfile => Path.Combine(_root, "profile");
        public string AppData => Path.Combine(_root, "roaming");
        public string LocalAppData => Path.Combine(_root, "local");

        public Fixture()
        {
            Directory.CreateDirectory(UserProfile);
            Directory.CreateDirectory(AppData);
            Directory.CreateDirectory(LocalAppData);
        }

        public AiEnvironmentQuota Environment(string id)
        {
            var snapshot = new AiQuotaService(UserProfile, AppData, LocalAppData).InspectLocalQuotas();
            return snapshot.Environments.SingleOrDefault(e => e.Id == id)
                ?? throw new InvalidOperationException($"Expected environment '{id}' was not detected from the isolated fixture.");
        }

        public string DatabasePath(string application) => Path.Combine(AppData, application, "User", "globalStorage", "state.vscdb");

        public SqliteConnection OpenDatabase(string application)
        {
            string path = DatabasePath(application);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ConnectionString);
            connection.Open();
            Execute(connection, "CREATE TABLE IF NOT EXISTS ItemTable (key TEXT PRIMARY KEY, value BLOB);");
            return connection;
        }

        public void WriteDatabase(string application, string key, string value)
        {
            using var connection = OpenDatabase(application);
            Insert(connection, key, value);
        }

        public void AddDatabaseRow(string application, string key, string value) => WriteDatabase(application, key, value);

        public void WriteSession(params string[] lines)
        {
            string directory = Path.Combine(UserProfile, ".codex", "sessions", "2026", "10", "01");
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "rollout-regression.jsonl"), lines);
        }

        public void Dispose()
        {
            // _root is allocated exclusively by this fixture under the OS temporary directory.
            string root = Path.GetFullPath(_root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Require(root.StartsWith(temp, StringComparison.OrdinalIgnoreCase), "Fixture cleanup must remain inside the OS temporary directory.");
            Directory.Delete(root, recursive: true);
        }
    }
}
