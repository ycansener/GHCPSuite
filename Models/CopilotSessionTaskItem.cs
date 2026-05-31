namespace GHCP.Suite.Models;

public sealed class CopilotSessionTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? DependsOnTaskId { get; set; }
    public bool CarryForward { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
