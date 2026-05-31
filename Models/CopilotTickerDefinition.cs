namespace GHCP.Suite.Models;

public sealed class CopilotTickerDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastOutputPath { get; set; }
    public string? LastError { get; set; }
}
