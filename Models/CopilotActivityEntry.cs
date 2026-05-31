namespace GHCP.Suite.Models;

public sealed class CopilotActivityEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Type { get; set; } = "note";
    public string Title { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string? SessionName { get; set; }
    public string? Project { get; set; }
    public string? AgentName { get; set; }
    public string? FilePath { get; set; }
}
