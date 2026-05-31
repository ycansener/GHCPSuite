using System.ComponentModel;
using System.Diagnostics;
using GHCP.Suite.Models;

namespace GHCP.Suite.Services;

public interface ICopilotConfigEditorService
{
    CopilotEditorLaunchResult OpenFile(CopilotFileEntry file);
    CopilotEditorLaunchResult OpenPath(string filePath, string displayName);
}

public sealed class CopilotConfigEditorService : ICopilotConfigEditorService
{
    private readonly string? _vsCodePath = ResolveVsCodePath();

    public CopilotEditorLaunchResult OpenFile(CopilotFileEntry file)
        => OpenPath(file.FullPath, file.Name);

    public CopilotEditorLaunchResult OpenPath(string filePath, string displayName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath)))
        {
            return new CopilotEditorLaunchResult(false, $"Cannot open {displayName} because the path does not exist.");
        }

        var startInfo = CreateStartInfo(filePath, out var launcherName);

        try
        {
            var process = Process.Start(startInfo);
            return process is null
                ? new CopilotEditorLaunchResult(false, $"Windows did not start {launcherName} for {displayName}.")
                : new CopilotEditorLaunchResult(true, $"Opened {displayName} in {launcherName}.");
        }
        catch (Win32Exception ex)
        {
            return new CopilotEditorLaunchResult(false, $"Failed to start {launcherName}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new CopilotEditorLaunchResult(false, $"Failed to start {launcherName}: {ex.Message}");
        }
    }

    private ProcessStartInfo CreateStartInfo(string filePath, out string launcherName)
    {
        if (Directory.Exists(filePath))
        {
            launcherName = "File Explorer";
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{EscapeDoubleQuotedArgument(filePath)}\"",
                UseShellExecute = true
            };
        }

        if (!string.IsNullOrWhiteSpace(_vsCodePath))
        {
            launcherName = "VS Code";
            return new ProcessStartInfo
            {
                FileName = _vsCodePath,
                Arguments = $"--reuse-window \"{EscapeDoubleQuotedArgument(filePath)}\"",
                UseShellExecute = true
            };
        }

        launcherName = "the default editor";
        return new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        };
    }

    private static string? ResolveVsCodePath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var entry in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var executable in new[] { "code.cmd", "code.exe" })
                {
                    var candidate = Path.Combine(entry, executable);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var candidates = new[]
            {
                Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(localAppData, "Programs", "VS Code", "Code.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var root in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = Path.Combine(root, "Microsoft VS Code", "Code.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string EscapeDoubleQuotedArgument(string value) => value.Replace("\"", "\\\"", StringComparison.Ordinal);
}
