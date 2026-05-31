using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GHCP.Suite.Models;
using Microsoft.Extensions.Options;

namespace GHCP.Suite.Services;

public interface ICopilotWorkspaceLaunchService
{
    CopilotResumeLaunchResult StartWorkspaceSession(CopilotWorkspace workspace);
}

public sealed class CopilotWorkspaceLaunchService(
    IOptionsMonitor<CopilotSuiteOptions> options,
    ICopilotEnvironmentService environmentService) : ICopilotWorkspaceLaunchService
{
    private readonly string _copilotCommandPath = ResolveCopilotCommandPath();
    private readonly string _powerShellCommandPath = ResolvePowerShellCommandPath();
    private readonly string? _windowsTerminalPath = ResolveWindowsTerminalPath();

    public CopilotResumeLaunchResult StartWorkspaceSession(CopilotWorkspace workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.Name))
        {
            return new CopilotResumeLaunchResult(false, "This workspace cannot be launched because it has no name.");
        }

        if (string.IsNullOrWhiteSpace(workspace.RootPath) || !Directory.Exists(workspace.RootPath))
        {
            return new CopilotResumeLaunchResult(false, $"Cannot start {workspace.Name} because {workspace.RootPath} does not exist.");
        }

        var workingDirectory = ResolveWorkingDirectory(workspace.RootPath, options.CurrentValue.StartupDirectory, environmentService.GetPaths().CopilotHome);
        var commandPath = EscapePowerShellSingleQuoted(_copilotCommandPath);
        var script = $"Set-Location -LiteralPath '{EscapePowerShellSingleQuoted(workingDirectory)}'; & '{commandPath}'";
        var encodedCommand = EncodePowerShellCommand(script);
        var startInfo = CreateStartInfo(workspace, workingDirectory, encodedCommand, options.CurrentValue.PreferWindowsTerminal, out var launcherName);

        try
        {
            var process = Process.Start(startInfo);
            return process is null
                ? new CopilotResumeLaunchResult(false, $"Windows did not start {launcherName} for {workspace.Name}.")
                : new CopilotResumeLaunchResult(true, $"Opened {launcherName} for {workspace.Name}.");
        }
        catch (Win32Exception ex)
        {
            return new CopilotResumeLaunchResult(false, $"Failed to start {launcherName}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new CopilotResumeLaunchResult(false, $"Failed to start {launcherName}: {ex.Message}");
        }
    }

    private ProcessStartInfo CreateStartInfo(
        CopilotWorkspace workspace,
        string workingDirectory,
        string encodedCommand,
        bool preferWindowsTerminal,
        out string launcherName)
    {
        if (preferWindowsTerminal && !string.IsNullOrWhiteSpace(_windowsTerminalPath))
        {
            launcherName = "Windows Terminal";
            return new ProcessStartInfo
            {
                FileName = _windowsTerminalPath,
                Arguments =
                    $"-w 0 new-tab --title \"{EscapeDoubleQuotedArgument(workspace.Name)}\" --startingDirectory \"{EscapeDoubleQuotedArgument(workingDirectory)}\" " +
                    $"\"{EscapeDoubleQuotedArgument(_powerShellCommandPath)}\" -NoExit -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            };
        }

        launcherName = "PowerShell";
        return new ProcessStartInfo
        {
            FileName = _powerShellCommandPath,
            Arguments = $"-NoExit -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
    }

    private static string ResolveWorkingDirectory(string workingDirectory, string? startupDirectory, string copilotHome)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            return workingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(startupDirectory) && Directory.Exists(startupDirectory))
        {
            return startupDirectory;
        }

        if (Directory.Exists(copilotHome))
        {
            return copilotHome;
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolveCopilotCommandPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var entry in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(entry, "copilot.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return "copilot.exe";
    }

    private static string? ResolveWindowsTerminalPath()
    {
        var windowsAppsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "wt.exe");

        if (File.Exists(windowsAppsPath))
        {
            return windowsAppsPath;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var entry in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(entry, "wt.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string ResolvePowerShellCommandPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var entry in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pwshCandidate = Path.Combine(entry, "pwsh.exe");
                if (File.Exists(pwshCandidate))
                {
                    return pwshCandidate;
                }

                var powershellCandidate = Path.Combine(entry, "powershell.exe");
                if (File.Exists(powershellCandidate))
                {
                    return powershellCandidate;
                }
            }
        }

        var programFilesPwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell",
            "7",
            "pwsh.exe");

        if (File.Exists(programFilesPwsh))
        {
            return programFilesPwsh;
        }

        return "powershell.exe";
    }

    private static string EncodePowerShellCommand(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string EscapePowerShellSingleQuoted(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeDoubleQuotedArgument(string value) => value.Replace("\"", "\\\"", StringComparison.Ordinal);
}
