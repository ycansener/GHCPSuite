using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GHCP.Suite.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace GHCP.Suite.Services;

public interface ICopilotSessionService
{
    Task<IReadOnlyList<CopilotSessionSummary>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CopilotAgentSummary>> GetAgentsAsync(CancellationToken cancellationToken = default);
    Task<CopilotSessionDetail?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed class CopilotSessionService(
    ICopilotEnvironmentService environmentService,
    IOptionsMonitor<CopilotSuiteOptions> optionsMonitor,
    ICopilotWorkDataService workDataService) : ICopilotSessionService
{
    public async Task<IReadOnlyList<CopilotSessionSummary>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        var paths = environmentService.GetPaths();
        var sessions = new Dictionary<string, CopilotSessionSummary>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in await LoadFileSystemSessionsAsync(paths, cancellationToken))
        {
            sessions[session.SessionId] = session;
        }

        foreach (var session in await LoadStoredSessionsAsync(paths, cancellationToken))
        {
            if (sessions.TryGetValue(session.SessionId, out var existing))
            {
                sessions[session.SessionId] = MergeSummaries(existing, session);
            }
            else
            {
                sessions[session.SessionId] = session;
            }
        }

        var data = await workDataService.GetDataAsync(cancellationToken);

        return sessions.Values
            .Select(session => ApplyWorkMetadata(
                session with
                {
                    Category = GetSessionCategory(optionsMonitor.CurrentValue.SessionCategories, session.SessionId)
                },
                data))
            .OrderByDescending(session => session.UpdatedAt ?? session.CreatedAt ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    public async Task<IReadOnlyList<CopilotAgentSummary>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessionsAsync(cancellationToken);

        return sessions
            .Where(session => session.IsActive || session.ToolExecutionCount > 0 || session.AssistantTurnCount > 0)
            .OrderByDescending(session => session.IsActive)
            .ThenByDescending(session => session.UpdatedAt ?? session.CreatedAt ?? DateTimeOffset.MinValue)
            .Take(12)
            .Select(session => new CopilotAgentSummary(
                session.SessionId,
                session.Name,
                session.WorkingDirectory,
                session.IsActive ? "Active" : "Idle",
                session.IsActive ? 1 : 0,
                session.ToolExecutionCount,
                session.AssistantTurnCount,
                session.UpdatedAt))
            .ToArray();
    }

    public async Task<CopilotSessionDetail?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessionsAsync(cancellationToken);
        var summary = sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (summary is null)
        {
            return null;
        }

        var paths = environmentService.GetPaths();
        var sessionDirectory = Path.Combine(paths.SessionStateDirectory, sessionId);
        var data = await workDataService.GetDataAsync(cancellationToken);
        var workItem = BuildWorkItem(summary, data);
        if (!Directory.Exists(sessionDirectory))
        {
            return new CopilotSessionDetail(summary, sessionDirectory, null, null, null, Array.Empty<string>(), Array.Empty<CopilotSessionFolderEntry>(), workItem);
        }

        var workspacePath = Path.Combine(sessionDirectory, "workspace.yaml");
        var planPath = Path.Combine(sessionDirectory, "plan.md");
        var checkpointIndexPath = Path.Combine(sessionDirectory, "checkpoints", "index.md");
        var eventsPath = Path.Combine(sessionDirectory, "events.jsonl");

        return new CopilotSessionDetail(
            summary,
            sessionDirectory,
            await ReadIfExistsAsync(workspacePath, cancellationToken),
            await ReadIfExistsAsync(planPath, cancellationToken),
            await ReadIfExistsAsync(checkpointIndexPath, cancellationToken),
            await ReadTailLinesAsync(eventsPath, 32, cancellationToken),
            LoadSessionFolderEntries(sessionDirectory, cancellationToken),
            workItem);
    }

    private static IReadOnlyList<CopilotSessionFolderEntry> LoadSessionFolderEntries(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sessionDirectory))
        {
            return Array.Empty<CopilotSessionFolderEntry>();
        }

        var entries = new List<CopilotSessionFolderEntry>();
        AppendSessionFolderEntries(sessionDirectory, sessionDirectory, entries, cancellationToken);
        return entries;
    }

    private static void AppendSessionFolderEntries(
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

            AppendSessionFolderEntries(rootDirectory, directory, entries, cancellationToken);
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

        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Count(character => character == Path.DirectorySeparatorChar);
    }

    private static async Task<IReadOnlyList<CopilotSessionSummary>> LoadFileSystemSessionsAsync(
        CopilotPaths paths,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.SessionStateDirectory))
        {
            return Array.Empty<CopilotSessionSummary>();
        }

        var directories = Directory.EnumerateDirectories(paths.SessionStateDirectory)
            .OrderByDescending(directory => Directory.GetLastWriteTimeUtc(directory))
            .ToArray();

        var sessions = new List<CopilotSessionSummary>(directories.Length);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sessions.Add(await BuildFileSystemSummaryAsync(directory, cancellationToken));
        }

        return sessions;
    }

    private static async Task<CopilotSessionSummary> BuildFileSystemSummaryAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        var sessionId = Path.GetFileName(sessionDirectory);
        var workspacePath = Path.Combine(sessionDirectory, "workspace.yaml");
        var eventsPath = Path.Combine(sessionDirectory, "events.jsonl");
        var sessionDbPath = Path.Combine(sessionDirectory, "session.db");
        var planPath = Path.Combine(sessionDirectory, "plan.md");
        var checkpointsPath = Path.Combine(sessionDirectory, "checkpoints");

        var workspace = await ParseWorkspaceAsync(workspacePath, cancellationToken);
        var events = await SummarizeEventsAsync(eventsPath, cancellationToken);
        var lockCount = Directory.EnumerateFiles(sessionDirectory, "inuse.*.lock", SearchOption.TopDirectoryOnly).Count();
        var checkpointCount = Directory.Exists(checkpointsPath)
            ? Directory.EnumerateFiles(checkpointsPath, "*.md", SearchOption.TopDirectoryOnly).Count()
            : 0;

        var workspaceName = GetWorkspaceValue(workspace, "name");
        var name = ChooseSessionTitle(workspaceName, events.FirstUserMessage, sessionId);
        var description = ChooseSessionDescription(name, events.FirstUserMessage, null, null);
        var workingDirectory = GetWorkspaceValue(workspace, "cwd") ?? sessionDirectory;
        var createdAt = ParseDateTimeOffset(GetWorkspaceValue(workspace, "created_at"));
        var updatedAt = ParseDateTimeOffset(GetWorkspaceValue(workspace, "updated_at")) ?? GetLatestWriteTime(sessionDirectory);
        var summaryCount = int.TryParse(GetWorkspaceValue(workspace, "summary_count"), out var count) ? count : 0;
        var userNamed = bool.TryParse(GetWorkspaceValue(workspace, "user_named"), out var named) && named;

        return new CopilotSessionSummary(
            sessionId,
            name,
            description,
            null,
            workingDirectory,
            "session-state",
            lockCount > 0,
            userNamed,
            File.Exists(workspacePath),
            File.Exists(eventsPath),
            File.Exists(sessionDbPath),
            File.Exists(planPath),
            summaryCount,
            checkpointCount,
            events.ToolExecutionCount,
            events.AssistantTurnCount,
            events.UserMessageCount,
            events.LastEventType,
            createdAt,
            updatedAt,
            null,
            "Planned",
            "Normal",
            null,
            0,
            0);
    }

    private static async Task<IReadOnlyList<CopilotSessionSummary>> LoadStoredSessionsAsync(
        CopilotPaths paths,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.SessionStoreDatabasePath))
        {
            return Array.Empty<CopilotSessionSummary>();
        }

        await using var connection = new SqliteConnection($"Data Source={paths.SessionStoreDatabasePath};Mode=ReadOnly");
        await connection.OpenAsync(cancellationToken);

        var tableExistsCommand = connection.CreateCommand();
        tableExistsCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='sessions';";
        var hasSessionsTable = Convert.ToInt32(await tableExistsCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
        if (!hasSessionsTable)
        {
            return Array.Empty<CopilotSessionSummary>();
        }

        var summaries = new List<CopilotSessionSummary>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.id,
                COALESCE(s.summary, '') AS summary,
                COALESCE(s.cwd, '') AS cwd,
                s.created_at,
                s.updated_at,
                COALESCE((SELECT t.user_message
                          FROM turns t
                          WHERE t.session_id = s.id
                          ORDER BY t.turn_index ASC
                          LIMIT 1), '') AS first_user_message,
                COALESCE((SELECT c.title
                          FROM checkpoints c
                          WHERE c.session_id = s.id
                          ORDER BY c.checkpoint_number DESC
                          LIMIT 1), '') AS checkpoint_title,
                COALESCE((SELECT c.overview
                          FROM checkpoints c
                          WHERE c.session_id = s.id
                          ORDER BY c.checkpoint_number DESC
                          LIMIT 1), '') AS checkpoint_overview
            FROM sessions s
            ORDER BY COALESCE(updated_at, created_at) DESC
            LIMIT 100;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sessionId = reader.GetString(0);
            var summary = reader.IsDBNull(1) ? null : reader.GetString(1);
            var firstUserMessage = reader.IsDBNull(5) ? null : reader.GetString(5);
            var checkpointTitle = reader.IsDBNull(6) ? null : reader.GetString(6);
            var checkpointOverview = reader.IsDBNull(7) ? null : reader.GetString(7);
            var name = ChooseSessionTitle(summary, checkpointTitle, firstUserMessage, sessionId);
            var description = ChooseSessionDescription(name, firstUserMessage, checkpointOverview, summary);

            summaries.Add(new CopilotSessionSummary(
                sessionId,
                name,
                description,
                null,
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                "session-store",
                false,
                false,
                false,
                false,
                false,
                false,
                0,
                0,
                0,
                0,
                0,
                null,
                ParseDateTimeOffset(reader.IsDBNull(3) ? null : reader.GetString(3)),
                ParseDateTimeOffset(reader.IsDBNull(4) ? null : reader.GetString(4)),
                null,
                "Planned",
                "Normal",
                null,
                0,
                0));
        }

        return summaries;
    }

    private static async Task<Dictionary<string, string>> ParseWorkspaceAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(workspacePath))
        {
            return result;
        }

        var lines = await File.ReadAllLinesAsync(workspacePath, cancellationToken);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }

    private static async Task<EventSummary> SummarizeEventsAsync(
        string eventsPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(eventsPath))
        {
            return new EventSummary(0, 0, 0, null, null);
        }

        var toolExecutions = 0;
        var assistantTurns = 0;
        var userMessages = 0;
        string? lastEventType = null;
        string? firstUserMessage = null;

        await using var stream = File.OpenRead(eventsPath);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var json = JsonDocument.Parse(line);
            if (!json.RootElement.TryGetProperty("type", out var typeElement))
            {
                continue;
            }

            var type = typeElement.GetString();
            lastEventType = type;

            switch (type)
            {
                case "tool.execution_start":
                    toolExecutions++;
                    break;
                case "assistant.turn_start":
                    assistantTurns++;
                    break;
                case "user.message":
                    userMessages++;
                    firstUserMessage ??= ExtractEventMessage(json.RootElement);
                    break;
            }
        }

        return new EventSummary(toolExecutions, assistantTurns, userMessages, lastEventType, firstUserMessage);
    }

    private static async Task<string?> ReadIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    private static async Task<IReadOnlyList<string>> ReadTailLinesAsync(string path, int maxLines, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        var queue = new Queue<string>(maxLines);
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            queue.Enqueue(line);
            while (queue.Count > maxLines)
            {
                queue.Dequeue();
            }
        }

        return queue.ToArray();
    }

    private static string? GetWorkspaceValue(IReadOnlyDictionary<string, string> workspace, string key)
    {
        return workspace.TryGetValue(key, out var value) ? value : null;
    }

    private static string? GetSessionCategory(IReadOnlyDictionary<string, string>? categories, string sessionId)
    {
        if (categories is null || !categories.TryGetValue(sessionId, out var category) || string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        return category.Trim();
    }

    private static CopilotSessionSummary ApplyWorkMetadata(CopilotSessionSummary summary, SuiteDataDocument data)
    {
        var workItem = BuildWorkItem(summary, data);
        var matchedWorkspace = data.Workspaces.FirstOrDefault(workspace => CopilotWorkService.WorkspaceMatches(workspace, summary.WorkingDirectory));
        return summary with
        {
            Project = string.IsNullOrWhiteSpace(workItem.Project)
                ? matchedWorkspace?.Name ?? CopilotWorkService.DeriveProject(summary.WorkingDirectory)
                : workItem.Project,
            WorkflowStatus = workItem.Status,
            Priority = workItem.Priority,
            NextAction = workItem.NextAction,
            OpenTaskCount = workItem.Tasks.Count(task => !string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase)),
            CompletedTaskCount = workItem.Tasks.Count(task => string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static CopilotSessionWorkItem BuildWorkItem(CopilotSessionSummary summary, SuiteDataDocument data)
    {
        var matchedWorkspace = data.Workspaces.FirstOrDefault(workspace => CopilotWorkService.WorkspaceMatches(workspace, summary.WorkingDirectory));
        if (!data.SessionWork.TryGetValue(summary.SessionId, out var workItem))
        {
            var fallback = CopilotWorkService.CreateDefaultWorkItem(summary);
            if (matchedWorkspace is not null)
            {
                fallback.Project = matchedWorkspace.Name;
            }

            return fallback;
        }

        return new CopilotSessionWorkItem
        {
            Project = string.IsNullOrWhiteSpace(workItem.Project)
                ? matchedWorkspace?.Name ?? CopilotWorkService.DeriveProject(summary.WorkingDirectory)
                : workItem.Project,
            Status = string.IsNullOrWhiteSpace(workItem.Status) ? (summary.IsActive ? "Active" : "Planned") : workItem.Status,
            Priority = string.IsNullOrWhiteSpace(workItem.Priority) ? "Normal" : workItem.Priority,
            NextAction = workItem.NextAction,
            Decisions = workItem.Decisions,
            Blockers = workItem.Blockers,
            NextSteps = workItem.NextSteps,
            Outcome = workItem.Outcome,
            Tasks = workItem.Tasks.Select(task => new CopilotSessionTaskItem
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
    }

    private static CopilotSessionSummary MergeSummaries(CopilotSessionSummary primary, CopilotSessionSummary secondary)
    {
        var mergedName = ChooseSessionTitle(
            primary.UserNamed ? primary.Name : null,
            secondary.Name,
            primary.Name,
            primary.SessionId);

        var mergedDescription = ChooseSessionDescription(
            mergedName,
            secondary.Description,
            primary.Description,
            null);

        return primary with
        {
            Name = mergedName,
            Description = mergedDescription,
            Category = primary.Category ?? secondary.Category,
            WorkingDirectory = string.IsNullOrWhiteSpace(primary.WorkingDirectory) ? secondary.WorkingDirectory : primary.WorkingDirectory,
            CreatedAt = primary.CreatedAt ?? secondary.CreatedAt,
            UpdatedAt = primary.UpdatedAt ?? secondary.UpdatedAt,
            SummaryCount = Math.Max(primary.SummaryCount, secondary.SummaryCount),
            CheckpointCount = Math.Max(primary.CheckpointCount, secondary.CheckpointCount),
            ToolExecutionCount = Math.Max(primary.ToolExecutionCount, secondary.ToolExecutionCount),
            AssistantTurnCount = Math.Max(primary.AssistantTurnCount, secondary.AssistantTurnCount),
            UserMessageCount = Math.Max(primary.UserMessageCount, secondary.UserMessageCount),
            LastEventType = primary.LastEventType ?? secondary.LastEventType
        };
    }

    private static string ChooseSessionTitle(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeText(candidate, 96);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return "Session";
    }

    private static string? ChooseSessionDescription(string title, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeText(candidate, 180);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !string.Equals(normalized, title, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? ExtractEventMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            return null;
        }

        return data.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..(maxLength - 1)].TrimEnd()}…";
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? GetLatestWriteTime(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var latest = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(directory))
            .Max();

        return new DateTimeOffset(latest, TimeSpan.Zero);
    }

    private sealed record EventSummary(
        int ToolExecutionCount,
        int AssistantTurnCount,
        int UserMessageCount,
        string? LastEventType,
        string? FirstUserMessage);
}
