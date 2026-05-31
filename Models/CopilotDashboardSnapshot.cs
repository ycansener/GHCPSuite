namespace GHCP.Suite.Models;

public sealed record CopilotDashboardSnapshot(
    int TotalSessions,
    int ActiveSessions,
    int BlockedSessions,
    int DoneSessions,
    int StalledSessions,
    int ResumedThisWeek,
    IReadOnlyList<CopilotProjectSummary> TopProjects,
    IReadOnlyList<CopilotAgentUsageSummary> TopAgents,
    IReadOnlyList<CopilotActivityEntry> RecentActivity);

public sealed record CopilotProjectSummary(
    string Name,
    int SessionCount,
    int ActiveCount,
    int BlockedCount,
    int DoneCount);

public sealed record CopilotAgentUsageSummary(
    string Name,
    int RunCount);
