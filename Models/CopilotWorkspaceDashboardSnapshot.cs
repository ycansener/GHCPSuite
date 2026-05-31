namespace GHCP.Suite.Models;

public sealed record CopilotWorkspaceDashboardSnapshot(
    int WorkspaceCount,
    int TotalSessions,
    int TotalTickers,
    int EnabledTickers,
    int TotalTrackedAgents,
    TimeSpan TotalTimeSpent,
    IReadOnlyList<CopilotWorkspaceTelemetry> Workspaces,
    IReadOnlyList<CopilotActivityEntry> RecentActivity);
