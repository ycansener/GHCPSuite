namespace GHCP.Suite.Models;

public sealed class CopilotWorkTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Planned";
    public string Priority { get; set; } = "Normal";
    public string? NextAction { get; set; }
    public List<CopilotWorkTemplateTask> Tasks { get; set; } = [];
}

public sealed class CopilotWorkTemplateTask
{
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public bool CarryForward { get; set; }
}
