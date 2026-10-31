using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Nokto.Core.Models;

namespace Nokto.Platform.Windows.Usage;

/// <summary>Solo initialize/initialized y account/rateLimits/read. Nunca abre chats ni modifica límites.</summary>
public sealed class CodexQuotaService : ICodexUsageReader
{
    public async Task<CodexUsageSnapshot> ReadAsync(CancellationToken cancellation)
    {
        string executable = FindExecutable() ?? throw new InvalidOperationException("No se encontró Codex. Instálalo o añade codex.exe al PATH.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(18));
        var token = timeout.Token;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetTempPath(), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--stdio");
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("No se pudo consultar Codex.");
        var drain = DrainErrors(process.StandardError, token);
        try
        {
            await process.StandardInput.WriteLineAsync("{\"id\":0,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"nokto_quota\",\"title\":\"Nokto\",\"version\":\"2.0\"}}}".AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
            using var init = await ReadReply(process.StandardOutput, 0, token);
            EnsureAccepted(init.RootElement);
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}".AsMemory(), token);
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"account/rateLimits/read\"}".AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
            using var reply = await ReadReply(process.StandardOutput, 1, token);
            EnsureAccepted(reply.RootElement);
            if (!reply.RootElement.TryGetProperty("result", out var result)) throw new InvalidDataException("Codex no entregó datos de cuota.");
            return CodexUsageSnapshot.Parse(result, DateTimeOffset.UtcNow);
        }
        finally
        {
            // Únicamente el proceso hijo que abrió esta consulta, nunca la aplicación Codex del usuario.
            process.StandardInput.Close();
            using var exit = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await process.WaitForExitAsync(exit.Token); }
            catch (OperationCanceledException) { try { process.Kill(entireProcessTree: false); } catch (InvalidOperationException) { } }
            timeout.Cancel(); try { await drain; } catch (OperationCanceledException) { } catch (IOException) { }
        }
    }
    private static void EnsureAccepted(JsonElement reply)
    {
        if (reply.TryGetProperty("error", out _)) throw new InvalidOperationException("Codex no pudo leer la cuota. Comprueba que tienes una sesión de ChatGPT iniciada.");
    }
    private static async Task<JsonDocument> ReadReply(StreamReader reader, int id, CancellationToken token)
    {
        for (int i = 0; i < 100; i++)
        {
            string? line = await reader.ReadLineAsync(token);
            if (line is null) throw new InvalidOperationException("Codex cerró la consulta. Abre Codex y comprueba tu sesión.");
            if (line.Length > 1_000_000) throw new InvalidDataException("Respuesta de Codex demasiado grande.");
            var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("id", out var identifier) &&
                identifier.ValueKind == JsonValueKind.Number && identifier.TryGetInt32(out int received) && received == id) return doc;
            doc.Dispose();
        }
        throw new InvalidDataException("Codex no respondió a la consulta de cuota.");
    }
    private static async Task DrainErrors(StreamReader reader, CancellationToken token)
    {
        // Descartar stderr: nunca guardar ni mostrar diagnósticos de sesión o credenciales.
        var buffer = new char[1024]; while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { }
    }
    private static string? FindExecutable()
    {
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        if (File.Exists(installed)) return installed;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { string candidate = Path.Combine(directory.Trim('"'), "codex.exe"); if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate)) return candidate; }
            catch (ArgumentException) { }
        }
        return null;
    }
}
