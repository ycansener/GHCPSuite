using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GHCP.Suite.Models;
using Microsoft.Extensions.Options;

namespace GHCP.Suite.Services;

public interface ICopilotAgentRunService
{
    Task<CopilotAgentLaunchResult> RunAgentAsync(CopilotDefinedAgent agent, CancellationToken cancellationToken = default);
}

public sealed class CopilotAgentRunService(
    IOptionsMonitor<CopilotSuiteOptions> options,
    ICopilotEnvironmentService environmentService,
    ICopilotWorkspaceAgentService workspaceAgentService,
    ICopilotWorkService workService) : ICopilotAgentRunService
{
    private readonly string _copilotCommandPath = ResolveCopilotCommandPath();
    private readonly string _powerShellCommandPath = ResolvePowerShellCommandPath();
    private readonly string? _windowsTerminalPath = ResolveWindowsTerminalPath();

    public async Task<CopilotAgentLaunchResult> RunAgentAsync(CopilotDefinedAgent agent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            return new CopilotAgentLaunchResult(false, "This agent cannot be run because it has no name.");
        }

        if (agent.IsWorkspaceScoped && !agent.Enabled)
        {
            return new CopilotAgentLaunchResult(false, $"{agent.DisplayName} is disabled for {agent.WorkspaceName ?? "this workspace"}.");
        }

        var workingDirectory = await ResolveWorkingDirectoryAsync(agent, cancellationToken);
        var commandPath = EscapePowerShellSingleQuoted(_copilotCommandPath);
        var agentName = EscapePowerShellSingleQuoted(agent.Name);
        var script = $"Set-Location -LiteralPath '{EscapePowerShellSingleQuoted(workingDirectory)}'; & '{commandPath}' --agent='{agentName}'";
        var encodedCommand = EncodePowerShellCommand(script);
        var startInfo = CreateStartInfo(agent, workingDirectory, encodedCommand, options.CurrentValue.PreferWindowsTerminal, out var launcherName);

        try
        {
            var process = Process.Start(startInfo);
            return process is null
                ? new CopilotAgentLaunchResult(false, $"Windows did not start {launcherName} for {agent.DisplayName}.")
                : new CopilotAgentLaunchResult(true, $"Opened {launcherName} for {agent.DisplayName}.");
        }
        catch (Win32Exception ex)
        {
            return new CopilotAgentLaunchResult(false, $"Failed to start {launcherName}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new CopilotAgentLaunchResult(false, $"Failed to start {launcherName}: {ex.Message}");
        }
    }

    private ProcessStartInfo CreateStartInfo(
        CopilotDefinedAgent agent,
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
                    $"-w 0 new-tab --title \"{EscapeDoubleQuotedArgument(agent.DisplayName)}\" --startingDirectory \"{EscapeDoubleQuotedArgument(workingDirectory)}\" " +
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

    private async Task<string> ResolveWorkingDirectoryAsync(CopilotDefinedAgent agent, CancellationToken cancellationToken)
    {
        var paths = environmentService.GetPaths();

        if (agent.IsWorkspaceScoped)
        {
            if (!string.IsNullOrWhiteSpace(agent.WorkspaceAgentId))
            {
                await workspaceAgentService.SynchronizeWorkspaceAgentAsync(agent.WorkspaceAgentId, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(agent.WorkspaceId))
            {
                var workspace = await workService.GetWorkspaceAsync(agent.WorkspaceId, cancellationToken);
                if (workspace is not null && Directory.Exists(workspace.RootPath))
                {
                    return workspace.RootPath;
                }
            }
        }

        return ResolveFallbackWorkingDirectory(options.CurrentValue.StartupDirectory, paths.CopilotHome);
    }

    private static string ResolveFallbackWorkingDirectory(string? startupDirectory, string copilotHome)
    {
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
