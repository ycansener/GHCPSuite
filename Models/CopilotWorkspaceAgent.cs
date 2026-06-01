namespace GHCP.Suite.Models;

public sealed class CopilotWorkspaceAgent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? SourceAgentId { get; set; }
    public string? SourceAgentName { get; set; }
    public string? SourceLabel { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
