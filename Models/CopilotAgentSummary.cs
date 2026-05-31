namespace GHCP.Suite.Models;

public sealed record CopilotAgentSummary(
    string SessionId,
    string Name,
    string WorkingDirectory,
    string Status,
    int LockCount,
    int ToolExecutionCount,
    int AssistantTurnCount,
    DateTimeOffset? UpdatedAt);
