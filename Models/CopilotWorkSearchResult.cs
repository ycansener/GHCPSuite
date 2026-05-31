namespace GHCP.Suite.Models;

public sealed record CopilotWorkSearchResult(
    string SessionId,
    string SessionName,
    string MatchSource,
    string Excerpt,
    string Project,
    string Status);
