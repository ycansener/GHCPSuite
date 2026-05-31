using GHCP.Suite.Models;

namespace GHCP.Suite.Services;

public interface ICopilotWorkService
{
    Task<IReadOnlyList<CopilotWorkspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default);
    Task<CopilotWorkspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<CopilotWorkspace?> GetActiveWorkspaceAsync(CancellationToken cancellationToken = default);
    Task SetActiveWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task SaveWorkspaceAsync(CopilotWorkspace workspace, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotWorkspaceFolderCandidate>> GetAvailableWorkspaceFoldersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotWorkspaceFolderCandidate>> GetIgnoredWorkspaceFoldersAsync(CancellationToken cancellationToken = default);
    Task<CopilotWorkspace?> ImportWorkspaceFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task IgnoreWorkspaceFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task UnignoreWorkspaceFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<CopilotWorkspaceSnapshot?> GetWorkspaceSnapshotAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<CopilotWorkspaceDashboardSnapshot> GetWorkspaceDashboardAsync(CancellationToken cancellationToken = default);
    Task<CopilotSessionWorkItem> GetSessionWorkAsync(CopilotSessionSummary session, CancellationToken cancellationToken = default);
    Task SaveSessionWorkAsync(string sessionId, CopilotSessionWorkItem workItem, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotSavedView>> GetSavedViewsAsync(CancellationToken cancellationToken = default);
    Task SaveSavedViewAsync(CopilotSavedView view, CancellationToken cancellationToken = default);
    Task DeleteSavedViewAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotWorkTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task ApplyTemplateAsync(string sessionId, string templateId, CancellationToken cancellationToken = default);
    Task RecordActivityAsync(CopilotActivityEntry entry, CancellationToken cancellationToken = default);
    Task<CopilotDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotActivityEntry>> GetRecentActivityAsync(int maxItems = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotWorkSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

public sealed class CopilotWorkService(
    ICopilotWorkDataService workDataService,
    ICopilotSessionService sessionService,
    ICopilotSettingsService settingsService) : ICopilotWorkService
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<IReadOnlyList<CopilotWorkspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.Workspaces.Select(CloneWorkspace).ToArray();
    }

    public async Task<CopilotWorkspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.Workspaces
            .Where(workspace => TextComparer.Equals(workspace.Id, workspaceId))
            .Select(CloneWorkspace)
            .FirstOrDefault();
    }

    public async Task<CopilotWorkspace?> GetActiveWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var workspace = data.Workspaces.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, data.ActiveWorkspaceId));
        return workspace is null ? null : CloneWorkspace(workspace);
    }

    public async Task SetActiveWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        if (!data.Workspaces.Any(workspace => TextComparer.Equals(workspace.Id, workspaceId)))
        {
            return;
        }

        data.ActiveWorkspaceId = workspaceId.Trim();
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task SaveWorkspaceAsync(CopilotWorkspace workspace, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var normalizedPath = NormalizeWorkspaceRoot(workspace.RootPath);
        var matchingPathWorkspace = data.Workspaces.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.RootPath)
            && string.Equals(NormalizeWorkspaceRoot(candidate.RootPath), normalizedPath, StringComparison.OrdinalIgnoreCase));
        var item = new CopilotWorkspace
        {
            Id = string.IsNullOrWhiteSpace(workspace.Id)
                ? matchingPathWorkspace?.Id ?? Guid.NewGuid().ToString("N")
                : workspace.Id.Trim(),
            Name = workspace.Name.Trim(),
            RootPath = normalizedPath,
            Description = string.IsNullOrWhiteSpace(workspace.Description) ? null : workspace.Description.Trim(),
            CreatedAt = workspace.CreatedAt == default ? DateTimeOffset.UtcNow : workspace.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var existing = data.Workspaces.FindIndex(candidate => TextComparer.Equals(candidate.Id, item.Id));
        if (existing >= 0)
        {
            item.CreatedAt = data.Workspaces[existing].CreatedAt;
            data.Workspaces[existing] = item;
        }
        else
        {
            data.Workspaces.Add(item);
        }

        data.ActiveWorkspaceId = item.Id;
        data.IgnoredWorkspaceFolders = data.IgnoredWorkspaceFolders
            .Where(path => !string.Equals(NormalizeWorkspaceRoot(path), normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task<IReadOnlyList<CopilotWorkspaceFolderCandidate>> GetAvailableWorkspaceFoldersAsync(CancellationToken cancellationToken = default)
    {
        var rootDirectory = await GetWorkspaceRootDirectoryAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return Array.Empty<CopilotWorkspaceFolderCandidate>();
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        return Directory.GetDirectories(rootDirectory)
            .Select(NormalizeWorkspaceRoot)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !data.Workspaces.Any(workspace => string.Equals(NormalizeWorkspaceRoot(workspace.RootPath), path, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !data.IgnoredWorkspaceFolders.Any(ignored => string.Equals(NormalizeWorkspaceRoot(ignored), path, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopilotWorkspaceFolderCandidate(Path.GetFileName(path), path))
            .ToArray();
    }

    public async Task<IReadOnlyList<CopilotWorkspaceFolderCandidate>> GetIgnoredWorkspaceFoldersAsync(CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.IgnoredWorkspaceFolders
            .Select(NormalizeWorkspaceRoot)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopilotWorkspaceFolderCandidate(Path.GetFileName(path), path))
            .ToArray();
    }

    public async Task<CopilotWorkspace?> ImportWorkspaceFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeWorkspaceRoot(folderPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
        {
            return null;
        }

        var workspace = new CopilotWorkspace
        {
            Name = Path.GetFileName(normalizedPath),
            RootPath = normalizedPath
        };

        await SaveWorkspaceAsync(workspace, cancellationToken);
        return await GetActiveWorkspaceAsync(cancellationToken);
    }

    public async Task IgnoreWorkspaceFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeWorkspaceRoot(folderPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        if (data.Workspaces.Any(workspace => string.Equals(NormalizeWorkspaceRoot(workspace.RootPath), normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!data.IgnoredWorkspaceFolders.Any(path => string.Equals(NormalizeWorkspaceRoot(path), normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            data.IgnoredWorkspaceFolders.Add(normalizedPath);
            await workDataService.SaveDataAsync(data, cancellationToken);
        }
    }

    public async Task UnignoreWorkspaceFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeWorkspaceRoot(folderPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        data.IgnoredWorkspaceFolders = data.IgnoredWorkspaceFolders
            .Where(path => !string.Equals(NormalizeWorkspaceRoot(path), normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task<CopilotWorkspaceSnapshot?> GetWorkspaceSnapshotAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await GetWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        var sessions = await sessionService.GetSessionsAsync(cancellationToken);
        var matchingSessions = sessions
            .Where(session => WorkspaceMatches(workspace, session.WorkingDirectory))
            .OrderByDescending(session => session.UpdatedAt ?? session.CreatedAt ?? DateTimeOffset.MinValue)
            .ToArray();

        var data = await workDataService.GetDataAsync(cancellationToken);
        var matchingTickers = data.Tickers
            .Where(ticker => TextComparer.Equals(ticker.WorkspaceId, workspace.Id))
            .Select(CloneTicker)
            .OrderBy(ticker => ticker.Name, TextComparer)
            .ToArray();

        var activity = data.ActivityHistory
            .Where(entry => TextComparer.Equals(entry.Project, workspace.Name)
                || matchingSessions.Any(session => TextComparer.Equals(session.SessionId, entry.SessionId)))
            .Take(12)
            .ToArray();

        var workspaceEntries = LoadFolderEntries(workspace.RootPath, cancellationToken);
        var telemetry = BuildWorkspaceTelemetry(workspace, matchingSessions, matchingTickers, activity, workspaceEntries);
        var agentsUsed = BuildAgentUsage(activity, matchingTickers);

        return new CopilotWorkspaceSnapshot(workspace, matchingSessions, activity, matchingTickers, agentsUsed, workspaceEntries, telemetry);
    }

    public async Task<CopilotWorkspaceDashboardSnapshot> GetWorkspaceDashboardAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await sessionService.GetSessionsAsync(cancellationToken);
        var data = await workDataService.GetDataAsync(cancellationToken);
        var workspaceTelemetry = data.Workspaces
            .Select(workspace =>
            {
                var matchingSessions = sessions
                    .Where(session => WorkspaceMatches(workspace, session.WorkingDirectory))
                    .ToArray();
                var matchingTickers = data.Tickers
                    .Where(ticker => TextComparer.Equals(ticker.WorkspaceId, workspace.Id))
                    .ToArray();
                var activity = data.ActivityHistory
                    .Where(entry => TextComparer.Equals(entry.Project, workspace.Name)
                        || matchingSessions.Any(session => TextComparer.Equals(session.SessionId, entry.SessionId)))
                    .ToArray();
                var workspaceEntries = LoadFolderEntries(workspace.RootPath, cancellationToken);
                return BuildWorkspaceTelemetry(workspace, matchingSessions, matchingTickers, activity, workspaceEntries);
            })
            .OrderByDescending(workspace => workspace.LastWorkedAt ?? DateTimeOffset.MinValue)
            .ThenBy(workspace => workspace.Name, TextComparer)
            .ToArray();

        return new CopilotWorkspaceDashboardSnapshot(
            workspaceTelemetry.Length,
            workspaceTelemetry.Sum(workspace => workspace.SessionCount),
            workspaceTelemetry.Sum(workspace => workspace.TickerCount),
            workspaceTelemetry.Sum(workspace => workspace.EnabledTickers),
            workspaceTelemetry.Sum(workspace => workspace.AgentCount),
            TimeSpan.FromTicks(workspaceTelemetry.Sum(workspace => workspace.TimeSpent.Ticks)),
            workspaceTelemetry,
            data.ActivityHistory.Take(20).ToArray());
    }

    public async Task<CopilotSessionWorkItem> GetSessionWorkAsync(CopilotSessionSummary session, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.SessionWork.TryGetValue(session.SessionId, out var stored)
            ? CloneWorkItem(stored)
            : CreateDefaultWorkItem(session);
    }

    public async Task SaveSessionWorkAsync(string sessionId, CopilotSessionWorkItem workItem, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        data.SessionWork[sessionId] = CloneWorkItem(workItem);
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task<IReadOnlyList<CopilotSavedView>> GetSavedViewsAsync(CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.SavedViews.Select(CloneSavedView).ToArray();
    }

    public async Task SaveSavedViewAsync(CopilotSavedView view, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var existing = data.SavedViews.FindIndex(candidate => TextComparer.Equals(candidate.Id, view.Id));
        if (existing >= 0)
        {
            data.SavedViews[existing] = CloneSavedView(view);
        }
        else
        {
            data.SavedViews.Add(CloneSavedView(view));
        }

        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task DeleteSavedViewAsync(string id, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        data.SavedViews = data.SavedViews
            .Where(view => !TextComparer.Equals(view.Id, id))
            .ToList();
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task<IReadOnlyList<CopilotWorkTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.Templates.Select(CloneTemplate).ToArray();
    }

    public async Task ApplyTemplateAsync(string sessionId, string templateId, CancellationToken cancellationToken = default)
    {
        var sessions = await sessionService.GetSessionsAsync(cancellationToken);
        var session = sessions.FirstOrDefault(candidate => TextComparer.Equals(candidate.SessionId, sessionId));
        if (session is null)
        {
            return;
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        var template = data.Templates.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, templateId));
        if (template is null)
        {
            return;
        }

        var workItem = data.SessionWork.TryGetValue(sessionId, out var stored)
            ? CloneWorkItem(stored)
            : CreateDefaultWorkItem(session);

        workItem.Status = template.Status;
        workItem.Priority = template.Priority;
        workItem.NextAction = template.NextAction ?? workItem.NextAction;

        foreach (var task in template.Tasks)
        {
            if (workItem.Tasks.Any(existing => TextComparer.Equals(existing.Title, task.Title)))
            {
                continue;
            }

            workItem.Tasks.Add(new CopilotSessionTaskItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = task.Title,
                Status = task.Status,
                CarryForward = task.CarryForward,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        data.SessionWork[sessionId] = workItem;
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task RecordActivityAsync(CopilotActivityEntry entry, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        data.ActivityHistory.Insert(0, new CopilotActivityEntry
        {
            Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
            Timestamp = entry.Timestamp == default ? DateTimeOffset.UtcNow : entry.Timestamp,
            Type = string.IsNullOrWhiteSpace(entry.Type) ? "note" : entry.Type,
            Title = entry.Title,
            SessionId = entry.SessionId,
            SessionName = entry.SessionName,
            Project = entry.Project,
            AgentName = entry.AgentName,
            FilePath = entry.FilePath
        });
        data.ActivityHistory = data.ActivityHistory
            .OrderByDescending(item => item.Timestamp)
            .Take(500)
            .ToList();
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task<CopilotDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await sessionService.GetSessionsAsync(cancellationToken);
        var data = await workDataService.GetDataAsync(cancellationToken);
        var recentActivity = data.ActivityHistory.Take(12).ToArray();

        var topProjects = sessions
            .GroupBy(session => string.IsNullOrWhiteSpace(session.Project) ? "No project" : session.Project!, TextComparer)
            .Select(group => new CopilotProjectSummary(
                group.Key,
                group.Count(),
                group.Count(session => TextComparer.Equals(session.WorkflowStatus, "Active")),
                group.Count(session => TextComparer.Equals(session.WorkflowStatus, "Blocked")),
                group.Count(session => TextComparer.Equals(session.WorkflowStatus, "Done"))))
            .OrderByDescending(project => project.SessionCount)
            .ThenBy(project => project.Name, TextComparer)
            .Take(5)
            .ToArray();

        var topAgents = data.ActivityHistory
            .Where(entry => TextComparer.Equals(entry.Type, "agent-run") && !string.IsNullOrWhiteSpace(entry.AgentName))
            .GroupBy(entry => entry.AgentName!, TextComparer)
            .Select(group => new CopilotAgentUsageSummary(group.Key, group.Count()))
            .OrderByDescending(agent => agent.RunCount)
            .ThenBy(agent => agent.Name, TextComparer)
            .Take(5)
            .ToArray();

        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
        var staleThreshold = DateTimeOffset.UtcNow.AddDays(-7);

        return new CopilotDashboardSnapshot(
            sessions.Count,
            sessions.Count(session => TextComparer.Equals(session.WorkflowStatus, "Active")),
            sessions.Count(session => TextComparer.Equals(session.WorkflowStatus, "Blocked")),
            sessions.Count(session => TextComparer.Equals(session.WorkflowStatus, "Done")),
            sessions.Count(session => !TextComparer.Equals(session.WorkflowStatus, "Done")
                && (session.UpdatedAt ?? session.CreatedAt ?? DateTimeOffset.UtcNow) < staleThreshold),
            data.ActivityHistory.Count(entry => TextComparer.Equals(entry.Type, "session-resume") && entry.Timestamp >= weekAgo),
            topProjects,
            topAgents,
            recentActivity);
    }

    public async Task<IReadOnlyList<CopilotActivityEntry>> GetRecentActivityAsync(int maxItems = 20, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.ActivityHistory.Take(maxItems).ToArray();
    }

    public async Task<IReadOnlyList<CopilotWorkSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return Array.Empty<CopilotWorkSearchResult>();
        }

        var sessions = await sessionService.GetSessionsAsync(cancellationToken);
        var results = new List<CopilotWorkSearchResult>();

        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var detail = await sessionService.GetSessionAsync(session.SessionId, cancellationToken);
            var matches = new (string Source, string? Content)[]
            {
                ("Session", session.Name),
                ("Summary", session.Description),
                ("Project", session.Project),
                ("Next action", session.NextAction),
                ("Decisions", detail?.WorkItem.Decisions),
                ("Blockers", detail?.WorkItem.Blockers),
                ("Next steps", detail?.WorkItem.NextSteps),
                ("Outcome", detail?.WorkItem.Outcome),
                ("Plan", detail?.PlanContent),
                ("Checkpoint", detail?.CheckpointIndexContent),
                ("Workspace", detail?.WorkspaceContent),
                ("Files", string.Join(Environment.NewLine, detail?.SessionEntries.Select(entry => entry.RelativePath) ?? [])),
                ("Tasks", string.Join(Environment.NewLine, detail?.WorkItem.Tasks.Select(task => task.Title) ?? []))
            };

            foreach (var (source, content) in matches)
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                var excerpt = BuildExcerpt(content, normalizedQuery);
                if (excerpt is null)
                {
                    continue;
                }

                results.Add(new CopilotWorkSearchResult(
                    session.SessionId,
                    session.Name,
                    source,
                    excerpt,
                    session.Project ?? "No project",
                    session.WorkflowStatus));
                break;
            }
        }

        return results
            .OrderBy(result => result.SessionName, TextComparer)
            .Take(30)
            .ToArray();
    }

    public static CopilotSessionWorkItem CreateDefaultWorkItem(CopilotSessionSummary session) => new()
    {
        Project = DeriveProject(session.WorkingDirectory),
        Status = session.IsActive ? "Active" : "Planned",
        Priority = "Normal",
        NextAction = null,
        Tasks = []
    };

    private static string? BuildExcerpt(string content, string query)
    {
        var index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = Math.Max(0, index - 48);
        var length = Math.Min(content.Length - start, Math.Max(query.Length + 96, 120));
        var excerpt = content.Substring(start, length).ReplaceLineEndings(" ");
        return excerpt.Trim();
    }

    public static string DeriveProject(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return "No project";
        }

        var trimmed = workingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var project = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(project) ? "No project" : project;
    }

    public static string NormalizeWorkspaceRoot(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        var trimmed = rootPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
        catch (NotSupportedException)
        {
            return trimmed;
        }
        catch (PathTooLongException)
        {
            return trimmed;
        }
    }

    public static bool WorkspaceMatches(CopilotWorkspace workspace, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workspace.RootPath) || string.IsNullOrWhiteSpace(workingDirectory))
        {
            return false;
        }

        var workspaceRoot = NormalizeWorkspaceRoot(workspace.RootPath);
        var sessionRoot = NormalizeWorkspaceRoot(workingDirectory);
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(sessionRoot))
        {
            return false;
        }

        return string.Equals(workspaceRoot, sessionRoot, StringComparison.OrdinalIgnoreCase)
            || sessionRoot.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static CopilotSessionWorkItem CloneWorkItem(CopilotSessionWorkItem source) => new()
    {
        Project = source.Project,
        Status = source.Status,
        Priority = source.Priority,
        NextAction = source.NextAction,
        Decisions = source.Decisions,
        Blockers = source.Blockers,
        NextSteps = source.NextSteps,
        Outcome = source.Outcome,
        Tasks = source.Tasks.Select(task => new CopilotSessionTaskItem
        {
            Id = task.Id,
            Title = task.Title,
            Status = task.Status,
            DependsOnTaskId = task.DependsOnTaskId,
            CarryForward = task.CarryForward,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        }).ToList()
    };

    private static CopilotSavedView CloneSavedView(CopilotSavedView source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        GroupBy = source.GroupBy,
        Status = source.Status,
        Project = source.Project,
        Category = source.Category,
        SearchText = source.SearchText
    };

    private static CopilotWorkTemplate CloneTemplate(CopilotWorkTemplate source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Description = source.Description,
        Status = source.Status,
        Priority = source.Priority,
        NextAction = source.NextAction,
        Tasks = source.Tasks.Select(task => new CopilotWorkTemplateTask
        {
            Title = task.Title,
            Status = task.Status,
            CarryForward = task.CarryForward
        }).ToList()
    };

    private static CopilotWorkspace CloneWorkspace(CopilotWorkspace source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        RootPath = source.RootPath,
        Description = source.Description,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static CopilotTickerDefinition CloneTicker(CopilotTickerDefinition source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        WorkspaceId = source.WorkspaceId,
        AgentName = source.AgentName,
        Prompt = source.Prompt,
        IntervalMinutes = source.IntervalMinutes,
        Enabled = source.Enabled,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        LastRunAt = source.LastRunAt,
        NextRunAt = source.NextRunAt,
        LastStatus = source.LastStatus,
        LastOutputPath = source.LastOutputPath,
        LastError = source.LastError
    };

    private static IReadOnlyList<CopilotSessionFolderEntry> LoadFolderEntries(string rootDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return Array.Empty<CopilotSessionFolderEntry>();
        }

        var entries = new List<CopilotSessionFolderEntry>();
        AppendFolderEntries(rootDirectory, rootDirectory, entries, cancellationToken);
        return entries;
    }

    private static void AppendFolderEntries(
        string rootDirectory,
        string currentDirectory,
        ICollection<CopilotSessionFolderEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(currentDirectory)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(rootDirectory, directory);
            entries.Add(new CopilotSessionFolderEntry(
                Path.GetFileName(directory),
                relativePath,
                directory,
                true,
                GetRelativeDepth(relativePath),
                new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero)));

            AppendFolderEntries(rootDirectory, directory, entries, cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(currentDirectory)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(rootDirectory, file);
            entries.Add(new CopilotSessionFolderEntry(
                Path.GetFileName(file),
                relativePath,
                file,
                false,
                GetRelativeDepth(relativePath),
                new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero)));
        }
    }

    private static int GetRelativeDepth(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return 0;
        }

        return relativePath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Length - 1;
    }

    private static CopilotWorkspaceTelemetry BuildWorkspaceTelemetry(
        CopilotWorkspace workspace,
        IReadOnlyList<CopilotSessionSummary> sessions,
        IReadOnlyList<CopilotTickerDefinition> tickers,
        IReadOnlyList<CopilotActivityEntry> activity,
        IReadOnlyList<CopilotSessionFolderEntry> entries)
    {
        return new CopilotWorkspaceTelemetry(
            workspace.Id,
            workspace.Name,
            workspace.RootPath,
            sessions.Count,
            sessions.Count(session => TextComparer.Equals(session.WorkflowStatus, "Active")),
            sessions.Count(session => TextComparer.Equals(session.WorkflowStatus, "Blocked")),
            tickers.Count,
            tickers.Count(ticker => ticker.Enabled),
            BuildAgentUsage(activity, tickers).Count,
            entries.Count(entry => !entry.IsDirectory),
            entries.Count(entry => entry.IsDirectory),
            ComputeTimeSpent(sessions),
            GetLastWorkedAt(workspace, sessions, activity, tickers));
    }

    private static IReadOnlyList<CopilotAgentUsageSummary> BuildAgentUsage(
        IReadOnlyList<CopilotActivityEntry> activity,
        IReadOnlyList<CopilotTickerDefinition> tickers)
    {
        var counts = activity
            .Where(entry => !string.IsNullOrWhiteSpace(entry.AgentName))
            .GroupBy(entry => entry.AgentName!, TextComparer)
            .ToDictionary(group => group.Key, group => group.Count(), TextComparer);

        foreach (var agentName in tickers
                     .Select(ticker => ticker.AgentName)
                     .Where(agentName => !string.IsNullOrWhiteSpace(agentName))
                     .Select(agentName => agentName!)
                     .Distinct(TextComparer))
        {
            counts.TryAdd(agentName, 0);
        }

        return counts
            .Select(item => new CopilotAgentUsageSummary(item.Key, item.Value))
            .OrderByDescending(agent => agent.RunCount)
            .ThenBy(agent => agent.Name, TextComparer)
            .ToArray();
    }

    private static TimeSpan ComputeTimeSpent(IReadOnlyList<CopilotSessionSummary> sessions)
    {
        var ticks = sessions.Sum(session =>
        {
            var started = session.CreatedAt ?? session.UpdatedAt;
            var ended = session.UpdatedAt ?? session.CreatedAt;
            if (started is null || ended is null || ended < started)
            {
                return 0L;
            }

            return (ended.Value - started.Value).Ticks;
        });

        return TimeSpan.FromTicks(ticks);
    }

    private static DateTimeOffset? GetLastWorkedAt(
        CopilotWorkspace workspace,
        IReadOnlyList<CopilotSessionSummary> sessions,
        IReadOnlyList<CopilotActivityEntry> activity,
        IReadOnlyList<CopilotTickerDefinition> tickers)
    {
        var candidates = new List<DateTimeOffset>();
        candidates.AddRange(sessions
            .Select(session => session.UpdatedAt ?? session.CreatedAt)
            .Where(value => value is not null)
            .Select(value => value!.Value));
        candidates.AddRange(activity.Select(entry => entry.Timestamp));
        candidates.AddRange(tickers
            .Select(ticker => ticker.LastRunAt ?? ticker.UpdatedAt));
        candidates.Add(workspace.UpdatedAt);

        return candidates.Count == 0 ? null : candidates.Max();
    }

    private async Task<string?> GetWorkspaceRootDirectoryAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetSettingsAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings.WorkspaceRootDirectory)
            ? settings.StartupDirectory
            : settings.WorkspaceRootDirectory;
    }
}
