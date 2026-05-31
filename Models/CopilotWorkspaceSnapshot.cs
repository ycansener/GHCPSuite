namespace GHCP.Suite.Models;

public sealed record CopilotWorkspaceSnapshot(
    CopilotWorkspace Workspace,
    IReadOnlyList<CopilotSessionSummary> Sessions,
    IReadOnlyList<CopilotActivityEntry> RecentActivity,
    IReadOnlyList<CopilotTickerDefinition> Tickers,
    IReadOnlyList<CopilotAgentUsageSummary> AgentsUsed,
    IReadOnlyList<CopilotSessionFolderEntry> WorkspaceEntries,
    CopilotWorkspaceTelemetry Telemetry);
