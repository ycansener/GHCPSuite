using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using GHCP.Suite.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GHCP.Suite.Services;

public interface ICopilotTickerService
{
    Task<IReadOnlyList<CopilotTickerDefinition>> GetTickersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotTickerRun>> GetTickerRunsAsync(string? tickerId = null, int maxItems = 30, CancellationToken cancellationToken = default);
    Task SaveTickerAsync(CopilotTickerDefinition ticker, CancellationToken cancellationToken = default);
    Task<CopilotTickerDefinition?> CloneTickerAsync(string tickerId, string workspaceId, CancellationToken cancellationToken = default);
    Task DeleteTickerAsync(string tickerId, CancellationToken cancellationToken = default);
    Task SetTickerEnabledAsync(string tickerId, bool enabled, CancellationToken cancellationToken = default);
    Task QueueTickerRunAsync(string tickerId, CancellationToken cancellationToken = default);
}

public sealed class CopilotTickerService(
    ICopilotWorkDataService workDataService,
    ICopilotWorkService workService,
    ICopilotWorkspaceAgentService workspaceAgentService,
    ICopilotAgentCatalogService agentCatalogService,
    ILogger<CopilotTickerService> logger) : BackgroundService, ICopilotTickerService
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;
    private readonly ConcurrentDictionary<string, byte> _runningTickers = new(TextComparer);
    private readonly string _copilotCommandPath = ResolveCopilotCommandPath();

    public async Task<IReadOnlyList<CopilotTickerDefinition>> GetTickersAsync(CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.Tickers.Select(CloneTicker).OrderBy(ticker => ticker.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<CopilotTickerRun>> GetTickerRunsAsync(string? tickerId = null, int maxItems = 30, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.TickerRuns
            .Where(run => string.IsNullOrWhiteSpace(tickerId) || TextComparer.Equals(run.TickerId, tickerId))
            .OrderByDescending(run => run.CompletedAt)
            .Take(maxItems)
            .Select(CloneRun)
            .ToArray();
    }

    public async Task SaveTickerAsync(CopilotTickerDefinition ticker, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var existing = data.Tickers.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, ticker.Id));
        var now = DateTimeOffset.UtcNow;
        var normalized = new CopilotTickerDefinition
        {
            Id = string.IsNullOrWhiteSpace(ticker.Id) ? Guid.NewGuid().ToString("N") : ticker.Id.Trim(),
            Name = ticker.Name.Trim(),
            WorkspaceId = ticker.WorkspaceId.Trim(),
            AgentName = string.IsNullOrWhiteSpace(ticker.AgentName) ? null : ticker.AgentName.Trim(),
            Prompt = ticker.Prompt.Trim(),
            IntervalMinutes = ticker.IntervalMinutes <= 0 ? 60 : ticker.IntervalMinutes,
            Enabled = ticker.Enabled,
            ClonedFromTickerId = existing?.ClonedFromTickerId ?? ticker.ClonedFromTickerId,
            ClonedFromTickerName = existing?.ClonedFromTickerName ?? ticker.ClonedFromTickerName,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            LastRunAt = existing?.LastRunAt,
            LastStatus = ticker.Enabled ? existing?.LastStatus : "Disabled",
            LastOutputPath = existing?.LastOutputPath,
            LastError = existing?.LastError,
            NextRunAt = ticker.Enabled
                ? existing?.NextRunAt ?? now.AddMinutes(ticker.IntervalMinutes <= 0 ? 60 : ticker.IntervalMinutes)
                : null
        };

        var index = data.Tickers.FindIndex(candidate => TextComparer.Equals(candidate.Id, normalized.Id));
        if (index >= 0)
        {
            data.Tickers[index] = normalized;
        }
        else
        {
            data.Tickers.Add(normalized);
        }

        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task<CopilotTickerDefinition?> CloneTickerAsync(string tickerId, string workspaceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tickerId) || string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        var source = data.Tickers.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, tickerId));
        if (source is null)
        {
            return null;
        }

        var workspace = await workService.GetWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var agentName = source.AgentName;
        if (!string.IsNullOrWhiteSpace(agentName) && !TextComparer.Equals(source.WorkspaceId, workspaceId))
        {
            var sourceWorkspaceAgent = await workspaceAgentService.GetWorkspaceAgentByInvocationNameAsync(source.WorkspaceId, agentName, cancellationToken);
            if (sourceWorkspaceAgent is not null)
            {
                var sourceAgent = await agentCatalogService.GetDefinedAgentAsync(sourceWorkspaceAgent.Id, cancellationToken);
                if (sourceAgent is not null)
                {
                    var clonedAgent = await workspaceAgentService.CloneAgentToWorkspaceAsync(sourceAgent, workspaceId, cancellationToken);
                    agentName = clonedAgent.Name;
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var clone = new CopilotTickerDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = GetUniqueTickerName(source.Name, workspaceId, data.Tickers),
            WorkspaceId = workspaceId,
            AgentName = agentName,
            Prompt = source.Prompt,
            IntervalMinutes = source.IntervalMinutes,
            Enabled = false,
            ClonedFromTickerId = source.Id,
            ClonedFromTickerName = source.Name,
            CreatedAt = now,
            UpdatedAt = now,
            LastStatus = "Disabled"
        };

        data.Tickers.Add(clone);
        await workDataService.SaveDataAsync(data, cancellationToken);
        return CloneTicker(clone);
    }

    public async Task DeleteTickerAsync(string tickerId, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        data.Tickers = data.Tickers.Where(ticker => !TextComparer.Equals(ticker.Id, tickerId)).ToList();
        data.TickerRuns = data.TickerRuns.Where(run => !TextComparer.Equals(run.TickerId, tickerId)).ToList();
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task SetTickerEnabledAsync(string tickerId, bool enabled, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var ticker = data.Tickers.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, tickerId));
        if (ticker is null)
        {
            return;
        }

        ticker.Enabled = enabled;
        ticker.UpdatedAt = DateTimeOffset.UtcNow;
        ticker.NextRunAt = enabled ? DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, ticker.IntervalMinutes)) : null;
        ticker.LastStatus = enabled ? ticker.LastStatus : "Disabled";
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task QueueTickerRunAsync(string tickerId, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var ticker = data.Tickers.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, tickerId));
        if (ticker is null)
        {
            return;
        }

        if (!ticker.Enabled)
        {
            ticker.LastStatus = "Disabled";
            ticker.UpdatedAt = DateTimeOffset.UtcNow;
            await workDataService.SaveDataAsync(data, cancellationToken);
            return;
        }

        if (!_runningTickers.TryAdd(tickerId, 0))
        {
            return;
        }

        ticker.LastStatus = "Queued";
        ticker.UpdatedAt = DateTimeOffset.UtcNow;
        await workDataService.SaveDataAsync(data, cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteTickerAsync(tickerId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ticker {TickerId} failed during background execution.", tickerId);
                await FinalizeTickerRunAsync(tickerId, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Failed", null, ex.Message, CancellationToken.None);
            }
            finally
            {
                _runningTickers.TryRemove(tickerId, out _);
            }
        }, CancellationToken.None);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await QueueDueTickersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ticker scheduler loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task QueueDueTickersAsync(CancellationToken cancellationToken)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var dueTickerIds = data.Tickers
            .Where(ticker => ticker.Enabled && ticker.NextRunAt is not null && ticker.NextRunAt <= now)
            .Select(ticker => ticker.Id)
            .ToArray();

        foreach (var tickerId in dueTickerIds)
        {
            await QueueTickerRunAsync(tickerId, cancellationToken);
        }
    }

    private async Task ExecuteTickerAsync(string tickerId, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var data = await workDataService.GetDataAsync(cancellationToken);
        var ticker = data.Tickers.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, tickerId));
        if (ticker is null)
        {
            return;
        }

        var workspace = await workService.GetWorkspaceAsync(ticker.WorkspaceId, cancellationToken);
        if (workspace is null || string.IsNullOrWhiteSpace(workspace.RootPath) || !Directory.Exists(workspace.RootPath))
        {
            await FinalizeTickerRunAsync(tickerId, null, startedAt, DateTimeOffset.UtcNow, "Failed", null, "Workspace folder not found.", cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(ticker.AgentName))
        {
            var workspaceAgent = await workspaceAgentService.GetWorkspaceAgentByInvocationNameAsync(workspace.Id, ticker.AgentName, cancellationToken);
            if (workspaceAgent is not null)
            {
                if (!workspaceAgent.Enabled)
                {
                    await FinalizeTickerRunAsync(
                        tickerId,
                        workspace,
                        startedAt,
                        DateTimeOffset.UtcNow,
                        "Skipped",
                        null,
                        "Agent disabled for this workspace.",
                        cancellationToken,
                        "Skipped because the referenced workspace agent is disabled.");
                    return;
                }

                await workspaceAgentService.SynchronizeWorkspaceAgentAsync(workspaceAgent.Id, cancellationToken);
            }
        }

        ticker.LastStatus = "Running";
        ticker.UpdatedAt = startedAt;
        ticker.LastError = null;
        await workDataService.SaveDataAsync(data, cancellationToken);

        CopilotWorkspaceStorage.EnsureWorkspaceStructure(workspace.RootPath);
        var outputDirectory = Path.Combine(CopilotWorkspaceStorage.GetTickersRoot(workspace.RootPath), SanitizePathSegment(ticker.Name));
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}.md");

        using var process = new Process
        {
            StartInfo = CreateStartInfo(ticker, workspace, outputPath)
        };

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var completedAt = DateTimeOffset.UtcNow;
            var status = process.ExitCode == 0 ? "Succeeded" : "Failed";
            var summary = BuildSummary(stdout, stderr);
            var error = process.ExitCode == 0 ? null : BuildSummary(stderr, stdout);

            await FinalizeTickerRunAsync(tickerId, workspace, startedAt, completedAt, status, outputPath, error, cancellationToken, summary);
        }
        catch (Win32Exception ex)
        {
            logger.LogError(ex, "Ticker {TickerId} failed to start Copilot CLI.", tickerId);
            await FinalizeTickerRunAsync(tickerId, workspace, startedAt, DateTimeOffset.UtcNow, "Failed", outputPath, ex.Message, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Ticker {TickerId} entered an invalid execution state.", tickerId);
            await FinalizeTickerRunAsync(tickerId, workspace, startedAt, DateTimeOffset.UtcNow, "Failed", outputPath, ex.Message, cancellationToken);
        }
    }

    private ProcessStartInfo CreateStartInfo(CopilotTickerDefinition ticker, CopilotWorkspace workspace, string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _copilotCommandPath,
            WorkingDirectory = workspace.RootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(workspace.RootPath);
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(ticker.Prompt);
        startInfo.ArgumentList.Add("--allow-all");
        startInfo.ArgumentList.Add("--no-ask-user");
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add($"ticker:{ticker.Name}");
        startInfo.ArgumentList.Add("--share");
        startInfo.ArgumentList.Add(outputPath);

        if (!string.IsNullOrWhiteSpace(ticker.AgentName))
        {
            startInfo.ArgumentList.Add("--agent");
            startInfo.ArgumentList.Add(ticker.AgentName);
        }

        return startInfo;
    }

    private async Task FinalizeTickerRunAsync(
        string tickerId,
        CopilotWorkspace? workspace,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        string? outputPath,
        string? error,
        CancellationToken cancellationToken,
        string? summary = null)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var ticker = data.Tickers.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, tickerId));
        if (ticker is null)
        {
            return;
        }

        ticker.LastRunAt = completedAt;
        ticker.NextRunAt = ticker.Enabled ? completedAt.AddMinutes(Math.Max(1, ticker.IntervalMinutes)) : null;
        ticker.LastStatus = status;
        ticker.LastOutputPath = outputPath;
        ticker.LastError = error;
        ticker.UpdatedAt = completedAt;

        data.TickerRuns.Insert(0, new CopilotTickerRun
        {
            Id = Guid.NewGuid().ToString("N"),
            TickerId = ticker.Id,
            TickerName = ticker.Name,
            WorkspaceId = ticker.WorkspaceId,
            WorkspaceName = workspace?.Name ?? string.Empty,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Status = status,
            OutputPath = outputPath,
            Summary = summary
        });
        data.TickerRuns = data.TickerRuns
            .OrderByDescending(run => run.CompletedAt)
            .Take(250)
            .ToList();

        await workDataService.SaveDataAsync(data, cancellationToken);
        await workService.RecordActivityAsync(new CopilotActivityEntry
        {
            Type = "ticker-run",
            Title = $"{ticker.Name} {status.ToLowerInvariant()}",
            Project = workspace?.Name,
            FilePath = outputPath
        }, cancellationToken);
    }

    private static string BuildSummary(string primary, string secondary)
    {
        var text = string.IsNullOrWhiteSpace(primary) ? secondary : primary;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No output";
        }

        var singleLine = string.Join(" ", text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= 180 ? singleLine : $"{singleLine[..179]}…";
    }

    private static string SanitizePathSegment(string value)
    {
        var characters = value
            .Trim()
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray();
        var sanitized = new string(characters).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "ticker" : sanitized;
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

    private static CopilotTickerDefinition CloneTicker(CopilotTickerDefinition source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        WorkspaceId = source.WorkspaceId,
        AgentName = source.AgentName,
        Prompt = source.Prompt,
        IntervalMinutes = source.IntervalMinutes,
        Enabled = source.Enabled,
        ClonedFromTickerId = source.ClonedFromTickerId,
        ClonedFromTickerName = source.ClonedFromTickerName,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        LastRunAt = source.LastRunAt,
        NextRunAt = source.NextRunAt,
        LastStatus = source.LastStatus,
        LastOutputPath = source.LastOutputPath,
        LastError = source.LastError
    };

    private static CopilotTickerRun CloneRun(CopilotTickerRun source) => new()
    {
        Id = source.Id,
        TickerId = source.TickerId,
        TickerName = source.TickerName,
        WorkspaceId = source.WorkspaceId,
        WorkspaceName = source.WorkspaceName,
        StartedAt = source.StartedAt,
        CompletedAt = source.CompletedAt,
        Status = source.Status,
        OutputPath = source.OutputPath,
        Summary = source.Summary
    };

    private static string GetUniqueTickerName(string sourceName, string workspaceId, IEnumerable<CopilotTickerDefinition> existingTickers)
    {
        var existingNames = new HashSet<string>(
            existingTickers
                .Where(ticker => TextComparer.Equals(ticker.WorkspaceId, workspaceId))
                .Select(ticker => ticker.Name),
            TextComparer);

        var baseName = $"{sourceName.Trim()} copy";
        var candidate = baseName;
        var suffix = 2;
        while (existingNames.Contains(candidate))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }
}
