namespace GHCP.Suite.Models;

public sealed class CopilotTickerRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TickerId { get; set; } = string.Empty;
    public string TickerName { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Queued";
    public string? OutputPath { get; set; }
    public string? Summary { get; set; }
}
