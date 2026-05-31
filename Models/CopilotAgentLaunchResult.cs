namespace GHCP.Suite.Models;

public sealed record CopilotAgentLaunchResult(
    bool Succeeded,
    string Message);
