namespace GHCP.Suite.Models;

public sealed record CopilotSessionDetail(
    CopilotSessionSummary Summary,
    string SessionDirectory,
    string? WorkspaceContent,
    string? PlanContent,
    string? CheckpointIndexContent,
    IReadOnlyList<string> RecentEvents,
    IReadOnlyList<CopilotSessionFolderEntry> SessionEntries,
    CopilotSessionWorkItem WorkItem);
