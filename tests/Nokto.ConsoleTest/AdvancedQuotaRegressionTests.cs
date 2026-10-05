using System.Text;
using Microsoft.Data.Sqlite;
using Nokto.Core.Services;

namespace Nokto.ConsoleTest;

internal static class AdvancedQuotaRegressionTests
{
    public static void Run()
    {
        foreach (string application in new[] { "Antigravity IDE", "Antigravity" })
        foreach (string extension in new[] { ".log", ".ldb" })
        {
            Test($"Antigravity {application} LevelDB {extension}: explicit model quota", f =>
            {
                f.WriteBytes(f.Roaming, Path.Combine(application, "Local Storage", "leveldb", "000001" + extension),
                    Combine([0, 7, 255], Encoding.UTF8.GetBytes("""{"model":"Gemini","fiveHourRemainingPercent":58,"weeklyRemainingPercent":93}"""), [0, 4, 255]));
                var quota = f.Environment("antigravity");
                AssertModel(quota, "Gemini", "Ventana 5h", 58);
                AssertModel(quota, "Gemini", "Semanal", 93);
            });
        }

        Test("Antigravity LevelDB: Gemini and Claude windows remain distinct", f =>
        {
            f.Write(f.Roaming, "Antigravity/Local Storage/leveldb/000002.log", """
                quota {"model":"Gemini","fiveHourRemainingPercent":58,"weeklyRemainingPercent":93}
                quota {"model":"Claude","fiveHourRemainingPercent":24,"weeklyRemainingPercent":71}
                """);
            var quota = f.Environment("antigravity");
            Require(quota.Metrics.Count == 4, "Both models must retain both explicit windows.");
            AssertModel(quota, "Gemini", "Ventana 5h", 58);
            AssertModel(quota, "Gemini", "Semanal", 93);
            AssertModel(quota, "Claude", "Ventana 5h", 24);
            AssertModel(quota, "Claude", "Semanal", 71);
        });

        Test("Antigravity logs: session folder date wins over stale file mtime", f =>
        {
            string oldLog = f.Write(f.Roaming, "Antigravity IDE/logs/20200101T000000/window1/quota.log",
                "quota {\"model\":\"Gemini\",\"fiveHourRemainingPercent\":5,\"weeklyRemainingPercent\":9}");
            f.Write(f.Roaming, "Antigravity IDE/logs/20261003T120000/window1/quota.log",
                "[quota response] {\"model\":\"Gemini\",\"fiveHourRemainingPercent\":58,\"weeklyRemainingPercent\":93}");
            File.SetLastWriteTimeUtc(oldLog, new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var quota = f.Environment("antigravity");
            AssertModel(quota, "Gemini", "Ventana 5h", 58);
            AssertModel(quota, "Gemini", "Semanal", 93);
        });

        Test("Antigravity profile logs: prefixed quota JSON is parsed", f =>
        {
            f.Write(f.Profile, ".antigravity/logs/quota.log", "[INFO balance] {\"model\":\"Claude\",\"fiveHourRemainingPercent\":31,\"weeklyRemainingPercent\":82}");
            var quota = f.Environment("antigravity");
            AssertModel(quota, "Claude", "Ventana 5h", 31);
            AssertModel(quota, "Claude", "Semanal", 82);
        });

        Test("Antigravity modelCredits Topic: explicit counters never imply percentages", f =>
        {
            byte[] topic = Topic(
                ("availableCreditsSentinelKey", VarintField(2, 2873)),
                ("minimumCreditAmountForUsageKey", VarintField(2, 50)));
            f.Database("antigravityUnifiedStateSync.modelCredits", Convert.ToBase64String(topic));
            var quota = f.Environment("antigravity");
            AssertUnknown(quota);
            Require(quota.Name == "Google Antigravity IDE" && quota.StatusNote == "Sesión lista",
                "Credit counters must not leak raw diagnostics into the launcher UI.");
        });

        Test("Antigravity modelCredits Topic: zero credits and a minimum of fifty are counters", f =>
        {
            f.Database("antigravityUnifiedStateSync.modelCredits", Convert.ToBase64String(Topic(
                ("availableCreditsSentinelKey", VarintField(2, 0)),
                ("minimumCreditAmountForUsageKey", VarintField(2, 50)))));
            var quota = f.Environment("antigravity");
            AssertUnknown(quota);
            Require(quota.StatusNote == "Sesión lista",
                "Zero credits and usage minimums must leave a clean launcher status without invented percentages.");
        });

        foreach (bool base64 in new[] { false, true })
        {
            Test($"Antigravity modelCredits {(base64 ? "base64 JSON" : "raw JSON BLOB")}: explicit metrics", f =>
            {
                byte[] json = Encoding.UTF8.GetBytes("""{"model":"Gemini","fiveHourRemainingPercent":58,"weeklyRemainingPercent":93}""");
                f.Database("antigravityUnifiedStateSync.modelCredits", base64 ? Convert.ToBase64String(json) : json);
                var quota = f.Environment("antigravity");
                AssertModel(quota, "Gemini", "Ventana 5h", 58);
                AssertModel(quota, "Gemini", "Semanal", 93);
            });
        }

        Test("Antigravity userStatus protobuf: model fractions without duration stay generic", f =>
        {
            f.Database("antigravityUnifiedStateSync.userStatus", Convert.ToBase64String(UserStatus(4102444800)));
            var quota = f.Environment("antigravity");
            Require(quota.HasExplicitQuotaMetrics && quota.Metrics.Count == 2, "Both real model fractions must supply current metrics.");
            AssertModel(quota, "Gemini", "Cuota", 58);
            AssertModel(quota, "Claude", "Cuota", 93);
            Require(quota.FiveHourUsagePercentage is null && quota.WeeklyUsagePercentage is null,
                "A fraction without a duration must not manufacture a five-hour or weekly quota.");
            Require(quota.Metrics.All(m => !m.WindowType.Contains("5h") && !m.WindowType.Contains("Semanal")),
                "Generic quota fractions must retain truthful labels.");
        });

        Test("Antigravity userStatus protobuf: expired model fractions supply no bars", f =>
        {
            f.Database("antigravityUnifiedStateSync.userStatus", Convert.ToBase64String(UserStatus(946684800)));
            AssertUnknown(f.Environment("antigravity"));
        });

        Test("Antigravity arbitrary binary integers: nearby model names do not imply quota", f =>
        {
            f.Database("antigravityUnifiedStateSync.modelCredits", Encoding.UTF8.GetBytes("Gemini\0\x32\0Claude\0\x64\0credits50"));
            f.Write(f.Roaming, "Antigravity/Local Storage/leveldb/000004.ldb", "Gemini quota credits remaining 58 window 93 malformed {\"weeklyRemainingPercent\":");
            AssertUnknown(f.Environment("antigravity"));
        });

        Test("Antigravity quota logs: user message text is not telemetry", f =>
        {
            f.Write(f.Profile, ".antigravity/logs/quota.log", """{"message":"Gemini 5h remaining: 58%"}""");
            AssertUnknown(f.Environment("antigravity"));
        });

        Test("Antigravity nested JSON: parent expiration applies to its quotas", f =>
        {
            f.Write(f.Profile, ".antigravity/state.json", """{"resetsAt":946684800,"model":"Gemini","quota":{"fiveHourRemainingPercent":58}}""");
            AssertUnknown(f.Environment("antigravity"));
        });

        foreach (string root in new[] { ".config/opencode", ".opencode", "LOCAL" })
        {
            Test($"OpenCode {root}: nested session token usage and explicit limit", f =>
            {
                string basePath = root == "LOCAL" ? f.Local : f.Profile;
                string relative = root == "LOCAL" ? "opencode/sessions/session.json" : root + "/sessions/session.json";
                f.Write(basePath, relative, """{"usage":{"totalTokens":25},"limits":{"totalTokens":100}}""");
                var quota = f.Environment("opencode");
                Require(quota.HasExplicitQuotaMetrics, "Usage and its declared limit must yield an explicit token quota.");
                Require(quota.Metrics.Any(m => m.WindowType.Contains("Tokens") && Math.Abs(m.RemainingPercent - 75) < 0.00001),
                    "25 of 100 consumed tokens leaves 75%, labeled Tokens.");
                Require(quota.FiveHourUsagePercentage is null && quota.WeeklyUsagePercentage is null,
                    "A local token allowance has no inferred duration.");
            });
        }

        Test("OpenCode provider auth state: explicit weekly quota without exposing credentials", f =>
        {
            f.Write(f.Profile, ".config/opencode/auth.json", """{"provider":{"apiKey":"regression-secret-never-display","weeklyUsagePercent":29}}""");
            var quota = f.Environment("opencode");
            Require(quota.HasExplicitQuotaMetrics && Math.Abs((quota.WeeklyUsagePercentage ?? -1) - 29) < 0.00001,
                "An explicitly reported provider weekly usage may supply the weekly bar.");
            Require(!quota.StatusNote.Contains("regression-secret-never-display") &&
                quota.Metrics.All(m => !m.DetailText.Contains("regression-secret-never-display")),
                "Evidence notes must not expose authentication material.");
        });

        Test("OpenCode token counter without limit: clean status and no percentage", f =>
        {
            f.Write(f.Profile, ".opencode/state.json", """{"tokensConsumed":12345}""");
            var quota = f.Environment("opencode");
            AssertUnknown(quota);
            Require(quota.Name == "OpenCode" && quota.StatusNote == "Entorno detectado",
                "Consumed tokens without a limit must not leak raw counters into the launcher UI.");
        });

        Test("OpenCode flat token counter and declared ceiling: truthful local percentage", f =>
        {
            f.Write(f.Profile, ".opencode/state.json", """{"tokensConsumed":25,"tokenLimit":100}""");
            var quota = f.Environment("opencode");
            Require(quota.Metrics.Any(m => m.WindowType == "Tokens" && Math.Abs(m.RemainingPercent - 75) < 0.00001),
                "An explicit consumed counter and its token limit leave 75%.");
            Require(quota.FiveHourUsagePercentage is null && quota.WeeklyUsagePercentage is null, "No duration was reported.");
        });

        Test("OpenCode JSONC model capacities: context and output are not consumed quotas", f =>
        {
            f.Write(f.Profile, ".config/opencode/opencode.jsonc", """
                { // model capacity configuration
                  "provider": { "local": { "models": { "Gemini": { "limit": { "context": 100, "output": 50 } } } } }
                }
                """);
            AssertUnknown(f.Environment("opencode"));
        });

        Test("OpenCode assets and skills: quoted examples are not telemetry", f =>
        {
            f.Write(f.Profile, ".config/opencode/assets/state.json", """{"weeklyUsagePercent":72}""");
            f.Write(f.Profile, ".config/opencode/skills/fixture/session.json", """{"windowUsagePercent":81}""");
            AssertUnknown(f.Environment("opencode"));
        });

        Test("OpenCode inspection boundaries: other profile trees cannot supply quota", f =>
        {
            f.Write(f.Profile, ".opencode/config.json", "{}");
            f.Write(f.Profile, "unrelated/opencode/state.json", """{"weeklyUsagePercent":72,"windowUsagePercent":81}""");
            f.Write(f.Roaming, "opencode/state.json", """{"weeklyUsagePercent":72,"windowUsagePercent":81}""");
            AssertUnknown(f.Environment("opencode"));
        });

        Test("OpenCode malformed and invalid limits: never manufacture remaining quotas", f =>
        {
            f.Write(f.Profile, ".opencode/state.json", """{"usage":{"totalTokens":25},"limits":{"totalTokens":0},"weeklyUsagePercent":101}""");
            f.Write(f.Profile, ".opencode/session.json", "{\"fiveHourRemainingPercent\":58,");
            AssertUnknown(f.Environment("opencode"));
        });
    }

    private static void AssertModel(AiEnvironmentQuota quota, string model, string window, double remaining)
    {
        Require(quota.HasExplicitQuotaMetrics, "Parsed quota must be explicit.");
        Require(quota.Metrics.Any(m => m.ModelName.Contains(model, StringComparison.OrdinalIgnoreCase)
            && m.WindowType.Contains(window, StringComparison.OrdinalIgnoreCase)
            && m.WindowType.Contains(model, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(m.RemainingPercent - remaining) < 0.0001),
            $"Expected {model} {window}: {remaining}% remaining and a visible model label.");
    }

    private static void AssertUnknown(AiEnvironmentQuota quota)
    {
        Require(!quota.HasExplicitQuotaMetrics && quota.Metrics.Count == 0,
            "Unsupported, expired or ambiguous evidence must not create a quota bar.");
        Require(quota.FiveHourUsagePercentage is null && quota.WeeklyUsagePercentage is null,
            "Unknown window percentages must remain null.");
    }

    private static byte[] UserStatus(ulong reset)
    {
        byte[] modelData = Combine(
            BytesField(1, Model("Gemini", .58f, reset)),
            BytesField(1, Model("Claude", .93f, reset)));
        return Topic(("userStatusSentinelKey", BytesField(33, modelData)));
    }

    private static byte[] Model(string name, float remaining, ulong reset)
    {
        byte[] quota = Combine(Varint((1u << 3) | 5), BitConverter.GetBytes(remaining),
            BytesField(2, VarintField(1, reset)));
        return Combine(BytesField(1, Encoding.UTF8.GetBytes(name)), BytesField(15, quota));
    }

    private static byte[] Topic(params (string Key, byte[] Primitive)[] entries) => Combine(entries.Select(entry =>
        BytesField(1, Combine(BytesField(1, Encoding.UTF8.GetBytes(entry.Key)),
            BytesField(2, BytesField(1, Encoding.UTF8.GetBytes(Convert.ToBase64String(entry.Primitive))))))).ToArray());

    private static byte[] BytesField(uint field, byte[] payload) => Combine(Varint((field << 3) | 2), Varint((ulong)payload.Length), payload);
    private static byte[] VarintField(uint field, ulong value) => Combine(Varint(field << 3), Varint(value));
    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            byte next = (byte)(value & 0x7f);
            value >>= 7;
            bytes.Add(value > 0 ? (byte)(next | 0x80) : next);
        } while (value > 0);
        return bytes.ToArray();
    }

    private static byte[] Combine(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

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
            throw new InvalidOperationException($"Advanced quota regression failed: {name}. {ex.Message}", ex);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"nokto_advanced_quota_{Guid.NewGuid():N}");
        public string Profile => Path.Combine(_root, "profile");
        public string Roaming => Path.Combine(_root, "roaming");
        public string Local => Path.Combine(_root, "local");

        public Fixture()
        {
            Directory.CreateDirectory(Profile);
            Directory.CreateDirectory(Roaming);
            Directory.CreateDirectory(Local);
        }

        public string Write(string root, string relative, string text) => WriteBytes(root, relative, Encoding.UTF8.GetBytes(text));

        public string WriteBytes(string root, string relative, byte[] bytes)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Database(string key, object value)
        {
            string path = Path.Combine(Roaming, "Antigravity", "User", "globalStorage", "state.vscdb");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
            }.ConnectionString);
            connection.Open();
            using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE IF NOT EXISTS ItemTable (key TEXT PRIMARY KEY, value BLOB);";
            create.ExecuteNonQuery();
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO ItemTable (key, value) VALUES ($key, $value);";
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$value", value);
            insert.ExecuteNonQuery();
        }

        public AiEnvironmentQuota Environment(string id) => new AiQuotaService(Profile, Roaming, Local)
            .InspectLocalQuotas().Environments.SingleOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException($"Environment '{id}' was not detected from its isolated data.");

        public void Dispose()
        {
            string root = Path.GetFullPath(_root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Require(root.StartsWith(temp, StringComparison.OrdinalIgnoreCase), "Fixture cleanup must remain in the OS temporary directory.");
            Directory.Delete(root, recursive: true);
        }
    }
}
