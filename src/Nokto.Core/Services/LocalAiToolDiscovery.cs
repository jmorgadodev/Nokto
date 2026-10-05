using Microsoft.Win32;

namespace Nokto.Core.Services;

public sealed record LocalAiTool(string Id, string Name, string ExecutablePath);

/// <summary>Bounded, passive discovery of installed launchers. Never starts a client or queries a model API.</summary>
public sealed class LocalAiToolDiscovery(string userProfile, string appData, string localAppData,
    bool includeSystemInstalls = false, IEnumerable<string>? installRoots = null, IEnumerable<string>? pathDirectories = null)
{
    private sealed record Tool(string Id, string Name, string[] Folders, string[] Executables);
    private static readonly Tool[] Catalog =
    [
        new("antigravity", "Google Antigravity IDE", ["Antigravity IDE", "Antigravity"], ["Antigravity IDE.exe", "Antigravity.exe"]),
        new("codex", "Codex Desktop / VS Code", ["Codex"], ["Codex.exe"]),
        new("opencode", "OpenCode", ["@opencode-aidesktop", "OpenCode", "opencode"], ["OpenCode.exe", "opencode.exe", "opencode.cmd"]),
        new("claude", "Claude", ["Claude", "Claude-3p"], ["Claude.exe", "claude.exe", "claude.cmd"]),
        new("lmstudio", "LM Studio", ["LM Studio", "LMStudio", "lm-studio"], ["LM Studio.exe", "LMStudio.exe"]),
        new("ollama", "Ollama", ["Ollama"], ["ollama app.exe", "ollama.exe"]),
        new("cursor", "Cursor", ["Cursor", "cursor"], ["Cursor.exe"]),
        new("windsurf", "Windsurf", ["Windsurf"], ["Windsurf.exe"]),
        new("jan", "Jan", ["Jan", "jan"], ["Jan.exe", "jan.exe"]),
        new("gpt4all", "GPT4All", ["GPT4All", "gpt4all"], ["gpt4all.exe", "chat.exe"]),
        new("msty", "Msty", ["Msty"], ["Msty.exe"]),
        new("anythingllm", "AnythingLLM", ["AnythingLLM", "anythingllm-desktop"], ["AnythingLLM.exe"]),
        new("chatgpt", "ChatGPT", ["ChatGPT"], ["ChatGPT.exe"])
    ];

    public IReadOnlyList<LocalAiTool> Discover()
    {
        var roots = new List<string> { Path.Combine(localAppData, "Programs"), localAppData, appData };
        roots.AddRange(installRoots ?? []);
        if (includeSystemInstalls)
            roots.AddRange(new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Where(p => p.Length > 0));
        var bins = new List<string> { Path.Combine(userProfile, ".local", "bin"), Path.Combine(userProfile, "scoop", "shims"),
            Path.Combine(appData, "npm"), Path.Combine(localAppData, "Microsoft", "WindowsApps") };
        bins.AddRange(pathDirectories ?? []);
        if (includeSystemInstalls)
            bins.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var registry = includeSystemInstalls && OperatingSystem.IsWindows() ? ReadRegisteredExecutables() : new Dictionary<string, string>();
        var result = new List<LocalAiTool>();
        foreach (var tool in Catalog)
        {
            string? executable = null;
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string folder in tool.Folders)
                {
                    string directory = Path.Combine(root, folder);
                    if (!IsLocalPath(directory)) continue;
                    executable = FindInDirectory(directory, tool.Executables);
                    if (executable is null)
                    {
                        try
                        {
                            // Squirrel/Qt packages keep the executable one directory below their installation root.
                            if (Directory.Exists(directory))
                                executable = Directory.EnumerateDirectories(directory).Take(64)
                                    .Select(child => FindInDirectory(child, tool.Executables)).FirstOrDefault(p => p is not null);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                    if (executable is not null) break;
                }
                if (executable is not null) break;
            }
            executable ??= registry.GetValueOrDefault(tool.Id);
            // Avoid generic names such as chat.exe in PATH; they could belong to an unrelated app.
            executable ??= bins.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(directory => FindInDirectory(directory, tool.Executables.Where(n => n != "chat.exe").ToArray()))
                .FirstOrDefault(p => p is not null);
            if (executable is not null) result.Add(new(tool.Id, tool.Name, executable));
        }
        return result;
    }

    private static string? FindInDirectory(string directory, string[] names)
    {
        directory = directory.Trim('"');
        if (!IsLocalPath(directory)) return null;
        return names.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
    }
    private static bool IsLocalPath(string path) => Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Dictionary<string, string> ReadRegisteredExecutables()
    {
        var result = new Dictionary<string, string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (string branch in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                try
                {
                    using var uninstall = hive.OpenSubKey(branch);
                    foreach (string key in uninstall?.GetSubKeyNames().Take(2048) ?? [])
                    {
                        using var app = uninstall!.OpenSubKey(key);
                        string name = app?.GetValue("DisplayName") as string ?? "";
                        var tool = Catalog.FirstOrDefault(t => name.Equals(t.Name, StringComparison.OrdinalIgnoreCase) ||
                            t.Folders.Any(f => name.Equals(f, StringComparison.OrdinalIgnoreCase) || name.StartsWith(f + " ", StringComparison.OrdinalIgnoreCase)));
                        if (tool is null || result.ContainsKey(tool.Id)) continue;
                        string directory = app?.GetValue("InstallLocation") as string ?? "";
                        string? executable = FindInDirectory(directory, tool.Executables);
                        string icon = (app?.GetValue("DisplayIcon") as string ?? "").Trim();
                        if (executable is null)
                        {
                            string path = icon.StartsWith('"') ? icon.Split('"').ElementAtOrDefault(1) ?? "" : icon.Split(',')[0];
                            if (IsLocalPath(path) && tool.Executables.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase) && File.Exists(path)) executable = path;
                        }
                        if (executable is not null) result[tool.Id] = executable;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        return result;
    }
}
