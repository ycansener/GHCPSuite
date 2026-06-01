using System.Text.Json;
using System.Text.Json.Serialization;
using GHCP.Suite.Models;

namespace GHCP.Suite.Services;

public interface ICopilotWorkDataService
{
    string GetDataFilePath();
    Task<SuiteDataDocument> GetDataAsync(CancellationToken cancellationToken = default);
    Task SaveDataAsync(SuiteDataDocument data, CancellationToken cancellationToken = default);
}

public sealed class CopilotWorkDataService(IHostEnvironment hostEnvironment) : ICopilotWorkDataService
{
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string GetDataFilePath() => Path.Combine(hostEnvironment.ContentRootPath, "suiteData.json");

    public async Task<SuiteDataDocument> GetDataAsync(CancellationToken cancellationToken = default)
    {
        await _fileGate.WaitAsync(cancellationToken);
        var filePath = GetDataFilePath();
        try
        {
            if (!File.Exists(filePath))
            {
                return CreateDefaultDocument();
            }

            await using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var data = await JsonSerializer.DeserializeAsync<SuiteDataDocument>(stream, SerializerOptions, cancellationToken);
            return Normalize(data ?? new SuiteDataDocument());
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task SaveDataAsync(SuiteDataDocument data, CancellationToken cancellationToken = default)
    {
        await _fileGate.WaitAsync(cancellationToken);
        var filePath = GetDataFilePath();
        try
        {
            await using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, Normalize(data), SerializerOptions, cancellationToken);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private static SuiteDataDocument CreateDefaultDocument() => Normalize(new SuiteDataDocument());

    private static SuiteDataDocument Normalize(SuiteDataDocument data)
    {
        data.Workspaces = NormalizeWorkspaces(data.Workspaces);
        data.IgnoredWorkspaceFolders = NormalizeIgnoredWorkspaceFolders(data.IgnoredWorkspaceFolders);
        data.WorkspaceAgents = NormalizeWorkspaceAgents(data.WorkspaceAgents);
        data.Tickers = NormalizeTickers(data.Tickers);
        data.TickerRuns = NormalizeTickerRuns(data.TickerRuns);
        data.SessionWork = NormalizeSessionWork(data.SessionWork);
        data.SavedViews = NormalizeSavedViews(data.SavedViews);
        data.Templates = NormalizeTemplates(data.Templates);
        data.ActivityHistory = NormalizeActivity(data.ActivityHistory);
        data.ActiveWorkspaceId = data.Workspaces.Any(workspace => string.Equals(workspace.Id, data.ActiveWorkspaceId, StringComparison.OrdinalIgnoreCase))
            ? data.ActiveWorkspaceId?.Trim()
            : data.Workspaces.FirstOrDefault()?.Id;
        return data;
    }

    private static List<CopilotTickerDefinition> NormalizeTickers(List<CopilotTickerDefinition>? source)
    {
        return (source ?? [])
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker.Name) && !string.IsNullOrWhiteSpace(ticker.WorkspaceId) && !string.IsNullOrWhiteSpace(ticker.Prompt))
            .Select(ticker =>
            {
                ticker.Id = string.IsNullOrWhiteSpace(ticker.Id) ? Guid.NewGuid().ToString("N") : ticker.Id.Trim();
                ticker.Name = ticker.Name.Trim();
                ticker.WorkspaceId = ticker.WorkspaceId.Trim();
                ticker.AgentName = NullIfBlank(ticker.AgentName);
                ticker.Prompt = ticker.Prompt.Trim();
                ticker.IntervalMinutes = ticker.IntervalMinutes <= 0 ? 60 : ticker.IntervalMinutes;
                ticker.ClonedFromTickerId = NullIfBlank(ticker.ClonedFromTickerId);
                ticker.ClonedFromTickerName = NullIfBlank(ticker.ClonedFromTickerName);
                ticker.CreatedAt = ticker.CreatedAt == default ? DateTimeOffset.UtcNow : ticker.CreatedAt;
                ticker.UpdatedAt = ticker.UpdatedAt == default ? ticker.CreatedAt : ticker.UpdatedAt;
                ticker.LastStatus = NullIfBlank(ticker.LastStatus);
                ticker.LastOutputPath = NullIfBlank(ticker.LastOutputPath);
                ticker.LastError = NullIfBlank(ticker.LastError);
                if (ticker.Enabled && ticker.NextRunAt is null)
                {
                    ticker.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(ticker.IntervalMinutes);
                }

                if (!ticker.Enabled)
                {
                    ticker.NextRunAt = null;
                }

                return ticker;
            })
            .OrderBy(ticker => ticker.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CopilotWorkspaceAgent> NormalizeWorkspaceAgents(List<CopilotWorkspaceAgent>? source)
    {
        return (source ?? [])
            .Where(agent =>
                !string.IsNullOrWhiteSpace(agent.WorkspaceId) &&
                !string.IsNullOrWhiteSpace(agent.Name) &&
                !string.IsNullOrWhiteSpace(agent.FileName))
            .Select(agent =>
            {
                agent.Id = string.IsNullOrWhiteSpace(agent.Id) ? Guid.NewGuid().ToString("N") : agent.Id.Trim();
                agent.WorkspaceId = agent.WorkspaceId.Trim();
                agent.Name = agent.Name.Trim();
                agent.DisplayName = string.IsNullOrWhiteSpace(agent.DisplayName) ? agent.Name : agent.DisplayName.Trim();
                agent.FileName = agent.FileName.Trim();
                agent.SourceAgentId = NullIfBlank(agent.SourceAgentId);
                agent.SourceAgentName = NullIfBlank(agent.SourceAgentName);
                agent.SourceLabel = NullIfBlank(agent.SourceLabel);
                agent.CreatedAt = agent.CreatedAt == default ? DateTimeOffset.UtcNow : agent.CreatedAt;
                agent.UpdatedAt = agent.UpdatedAt == default ? agent.CreatedAt : agent.UpdatedAt;
                return agent;
            })
            .GroupBy(agent => agent.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderBy(agent => agent.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CopilotTickerRun> NormalizeTickerRuns(List<CopilotTickerRun>? source)
    {
        return (source ?? [])
            .Where(run => !string.IsNullOrWhiteSpace(run.TickerId))
            .Select(run =>
            {
                run.Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id.Trim();
                run.TickerId = run.TickerId.Trim();
                run.TickerName = run.TickerName?.Trim() ?? string.Empty;
                run.WorkspaceId = run.WorkspaceId?.Trim() ?? string.Empty;
                run.WorkspaceName = run.WorkspaceName?.Trim() ?? string.Empty;
                run.Status = string.IsNullOrWhiteSpace(run.Status) ? "Queued" : run.Status.Trim();
                run.OutputPath = NullIfBlank(run.OutputPath);
                run.Summary = NullIfBlank(run.Summary);
                run.StartedAt = run.StartedAt == default ? DateTimeOffset.UtcNow : run.StartedAt;
                run.CompletedAt = run.CompletedAt == default ? run.StartedAt : run.CompletedAt;
                return run;
            })
            .OrderByDescending(run => run.CompletedAt)
            .Take(250)
            .ToList();
    }

    private static List<string> NormalizeIgnoredWorkspaceFolders(List<string>? source)
    {
        return (source ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CopilotWorkspace> NormalizeWorkspaces(List<CopilotWorkspace>? source)
    {
        return (source ?? [])
            .Where(workspace => !string.IsNullOrWhiteSpace(workspace.Name) && !string.IsNullOrWhiteSpace(workspace.RootPath))
            .Select(workspace =>
            {
                workspace.Id = string.IsNullOrWhiteSpace(workspace.Id) ? Guid.NewGuid().ToString("N") : workspace.Id.Trim();
                workspace.Name = workspace.Name.Trim();
                workspace.RootPath = workspace.RootPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                workspace.Description = NullIfBlank(workspace.Description);
                workspace.CreatedAt = workspace.CreatedAt == default ? DateTimeOffset.UtcNow : workspace.CreatedAt;
                workspace.UpdatedAt = workspace.UpdatedAt == default ? workspace.CreatedAt : workspace.UpdatedAt;
                return workspace;
            })
            .GroupBy(workspace => workspace.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderBy(workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, CopilotSessionWorkItem> NormalizeSessionWork(Dictionary<string, CopilotSessionWorkItem>? source)
    {
        var normalized = new Dictionary<string, CopilotSessionWorkItem>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return normalized;
        }

        foreach (var (sessionId, workItem) in source)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            normalized[sessionId.Trim()] = NormalizeWorkItem(workItem);
        }

        return normalized;
    }

    private static CopilotSessionWorkItem NormalizeWorkItem(CopilotSessionWorkItem? workItem)
    {
        workItem ??= new CopilotSessionWorkItem();
        workItem.Project = NullIfBlank(workItem.Project);
        workItem.Status = NormalizeStatus(workItem.Status);
        workItem.Priority = NormalizePriority(workItem.Priority);
        workItem.NextAction = NullIfBlank(workItem.NextAction);
        workItem.Decisions = NullIfBlank(workItem.Decisions);
        workItem.Blockers = NullIfBlank(workItem.Blockers);
        workItem.NextSteps = NullIfBlank(workItem.NextSteps);
        workItem.Outcome = NullIfBlank(workItem.Outcome);
        workItem.Tasks = (workItem.Tasks ?? [])
            .Where(task => !string.IsNullOrWhiteSpace(task.Title))
            .Select(NormalizeTask)
            .OrderBy(task => task.CreatedAt)
            .ToList();
        return workItem;
    }

    private static CopilotSessionTaskItem NormalizeTask(CopilotSessionTaskItem task)
    {
        task.Id = string.IsNullOrWhiteSpace(task.Id) ? Guid.NewGuid().ToString("N") : task.Id.Trim();
        task.Title = task.Title.Trim();
        task.Status = NormalizeTaskStatus(task.Status);
        task.DependsOnTaskId = NullIfBlank(task.DependsOnTaskId);
        task.CreatedAt = task.CreatedAt == default ? DateTimeOffset.UtcNow : task.CreatedAt;
        task.UpdatedAt = task.UpdatedAt == default ? task.CreatedAt : task.UpdatedAt;
        return task;
    }

    private static List<CopilotSavedView> NormalizeSavedViews(List<CopilotSavedView>? source)
    {
        return (source ?? [])
            .Where(view => !string.IsNullOrWhiteSpace(view.Name))
            .Select(view =>
            {
                view.Id = string.IsNullOrWhiteSpace(view.Id) ? Guid.NewGuid().ToString("N") : view.Id.Trim();
                view.Name = view.Name.Trim();
                view.GroupBy = NormalizeGroupBy(view.GroupBy);
                view.Status = NullIfBlank(view.Status);
                view.Project = NullIfBlank(view.Project);
                view.Category = NullIfBlank(view.Category);
                view.SearchText = NullIfBlank(view.SearchText);
                return view;
            })
            .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CopilotWorkTemplate> NormalizeTemplates(List<CopilotWorkTemplate>? source)
    {
        var templates = (source ?? []).Where(template => !string.IsNullOrWhiteSpace(template.Name)).ToList();
        if (templates.Count == 0)
        {
            templates = CreateDefaultTemplates();
        }

        return templates
            .Select(template =>
            {
                template.Id = string.IsNullOrWhiteSpace(template.Id) ? Guid.NewGuid().ToString("N") : template.Id.Trim();
                template.Name = template.Name.Trim();
                template.Description = template.Description.Trim();
                template.Status = NormalizeStatus(template.Status);
                template.Priority = NormalizePriority(template.Priority);
                template.NextAction = NullIfBlank(template.NextAction);
                template.Tasks = (template.Tasks ?? [])
                    .Where(task => !string.IsNullOrWhiteSpace(task.Title))
                    .Select(task =>
                    {
                        task.Title = task.Title.Trim();
                        task.Status = NormalizeTaskStatus(task.Status);
                        return task;
                    })
                    .ToList();
                return template;
            })
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CopilotActivityEntry> NormalizeActivity(List<CopilotActivityEntry>? source)
    {
        return (source ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Title))
            .Select(entry =>
            {
                entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
                entry.Type = string.IsNullOrWhiteSpace(entry.Type) ? "note" : entry.Type.Trim();
                entry.Title = entry.Title.Trim();
                entry.SessionId = NullIfBlank(entry.SessionId);
                entry.SessionName = NullIfBlank(entry.SessionName);
                entry.Project = NullIfBlank(entry.Project);
                entry.AgentName = NullIfBlank(entry.AgentName);
                entry.FilePath = NullIfBlank(entry.FilePath);
                entry.Timestamp = entry.Timestamp == default ? DateTimeOffset.UtcNow : entry.Timestamp;
                return entry;
            })
            .OrderByDescending(entry => entry.Timestamp)
            .Take(500)
            .ToList();
    }

    private static List<CopilotWorkTemplate> CreateDefaultTemplates() =>
    [
        new()
        {
            Id = "bugfix",
            Name = "Bugfix",
            Description = "Track diagnosis, fix, validation, and follow-up for a defect.",
            Status = "Active",
            Priority = "High",
            NextAction = "Reproduce the issue and isolate the failing path.",
            Tasks =
            [
                new() { Title = "Reproduce the issue", Status = "Pending" },
                new() { Title = "Identify root cause", Status = "Pending" },
                new() { Title = "Implement and verify fix", Status = "Pending" },
                new() { Title = "Document follow-up risks", Status = "Pending", CarryForward = true }
            ]
        },
        new()
        {
            Id = "feature-planning",
            Name = "Feature planning",
            Description = "Shape a feature from scope to implementation steps.",
            Status = "Planned",
            Priority = "Normal",
            NextAction = "Define scope, constraints, and success criteria.",
            Tasks =
            [
                new() { Title = "Define scope and requirements", Status = "Pending" },
                new() { Title = "Identify design and UX decisions", Status = "Pending" },
                new() { Title = "Break work into implementation steps", Status = "Pending" },
                new() { Title = "List validation and rollout tasks", Status = "Pending", CarryForward = true }
            ]
        },
        new()
        {
            Id = "market-research",
            Name = "Market research",
            Description = "Investigate opportunity, competitors, and validation signals.",
            Status = "Planned",
            Priority = "Normal",
            NextAction = "Clarify the target customer and core problem.",
            Tasks =
            [
                new() { Title = "Define target user and problem", Status = "Pending" },
                new() { Title = "Research competitors and alternatives", Status = "Pending" },
                new() { Title = "Summarize market signals and risks", Status = "Pending" },
                new() { Title = "Recommend next validation steps", Status = "Pending", CarryForward = true }
            ]
        },
        new()
        {
            Id = "refactor",
            Name = "Refactor",
            Description = "Organize technical cleanup into safe, staged work.",
            Status = "Planned",
            Priority = "Normal",
            NextAction = "Identify the highest-risk coupling to untangle first.",
            Tasks =
            [
                new() { Title = "Document current pain points", Status = "Pending" },
                new() { Title = "Define target structure", Status = "Pending" },
                new() { Title = "Implement incremental cleanup", Status = "Pending" },
                new() { Title = "Confirm no regression areas remain", Status = "Pending", CarryForward = true }
            ]
        },
        new()
        {
            Id = "release-prep",
            Name = "Release prep",
            Description = "Prepare a change set for release and handoff.",
            Status = "Active",
            Priority = "High",
            NextAction = "Check release blockers and unresolved validation items.",
            Tasks =
            [
                new() { Title = "Review blockers and risks", Status = "Pending" },
                new() { Title = "Confirm release notes", Status = "Pending" },
                new() { Title = "Validate deployment/readiness steps", Status = "Pending" },
                new() { Title = "Capture post-release follow-up", Status = "Pending", CarryForward = true }
            ]
        }
    ];

    private static string NormalizeStatus(string? value)
    {
        var normalized = value?.Trim();
        return normalized switch
        {
            "Active" => "Active",
            "Blocked" => "Blocked",
            "Done" => "Done",
            _ => "Planned"
        };
    }

    private static string NormalizePriority(string? value)
    {
        var normalized = value?.Trim();
        return normalized switch
        {
            "Low" => "Low",
            "High" => "High",
            "Critical" => "Critical",
            _ => "Normal"
        };
    }

    private static string NormalizeTaskStatus(string? value)
    {
        var normalized = value?.Trim();
        return normalized switch
        {
            "In Progress" => "In Progress",
            "Done" => "Done",
            "Blocked" => "Blocked",
            _ => "Pending"
        };
    }

    private static string NormalizeGroupBy(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "category" => "category",
            "status" => "status",
            _ => "project"
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
