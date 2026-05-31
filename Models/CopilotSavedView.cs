namespace GHCP.Suite.Models;

public sealed class CopilotSavedView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string GroupBy { get; set; } = "project";
    public string? Status { get; set; }
    public string? Project { get; set; }
    public string? Category { get; set; }
    public string? SearchText { get; set; }
}
