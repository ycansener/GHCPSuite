using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GHCP.Suite.Models;
using Microsoft.Extensions.Options;

namespace GHCP.Suite.Services;

public interface ICopilotResumeService
{
    CopilotResumeLaunchResult ResumeSession(CopilotSessionSummary session);
}

public sealed class CopilotResumeService(
    IOptionsMonitor<CopilotSuiteOptions> options,
    ICopilotEnvironmentService environmentService) : ICopilotResumeService
{
    private readonly string _copilotCommandPath = ResolveCopilotCommandPath();
    private readonly string _powerShellCommandPath = ResolvePowerShellCommandPath();
    private readonly string? _windowsTerminalPath = ResolveWindowsTerminalPath();

    public CopilotResumeLaunchResult ResumeSession(CopilotSessionSummary session)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            return new CopilotResumeLaunchResult(false, "This session cannot be resumed because it has no session id.");
        }

        var workingDirectory = ResolveWorkingDirectory(session.WorkingDirectory, options.CurrentValue.StartupDirectory, environmentService.GetPaths().SessionStateDirectory);
        var commandPath = EscapePowerShellSingleQuoted(_copilotCommandPath);
        var sessionId = EscapePowerShellSingleQuoted(session.SessionId);
        var script = $"Set-Location -LiteralPath '{EscapePowerShellSingleQuoted(workingDirectory)}'; & '{commandPath}' --resume='{sessionId}'";
        var encodedCommand = EncodePowerShellCommand(script);
        var startInfo = CreateStartInfo(session, workingDirectory, encodedCommand, options.CurrentValue.PreferWindowsTerminal, out var launcherName);

        try
        {
            var process = Process.Start(startInfo);
            return process is null
                ? new CopilotResumeLaunchResult(false, $"Windows did not start {launcherName} for {session.Name}.")
                : new CopilotResumeLaunchResult(true, $"Opened {launcherName} for {session.Name}.");
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
        CopilotSessionSummary session,
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
                    $"-w 0 new-tab --title \"{EscapeDoubleQuotedArgument(session.Name)}\" --startingDirectory \"{EscapeDoubleQuotedArgument(workingDirectory)}\" " +
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

    private static string ResolveWorkingDirectory(string workingDirectory, string? startupDirectory, string sessionStateDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            return workingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(startupDirectory) && Directory.Exists(startupDirectory))
        {
            return startupDirectory;
        }

        if (Directory.Exists(sessionStateDirectory))
        {
            return sessionStateDirectory;
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
