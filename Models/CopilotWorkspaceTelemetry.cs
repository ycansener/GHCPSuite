namespace GHCP.Suite.Models;

public sealed record CopilotWorkspaceTelemetry(
    string WorkspaceId,
    string Name,
    string RootPath,
    int SessionCount,
    int ActiveSessions,
    int BlockedSessions,
    int TickerCount,
    int EnabledTickers,
    int AgentCount,
    int FileCount,
    int DirectoryCount,
    TimeSpan TimeSpent,
    DateTimeOffset? LastWorkedAt);
