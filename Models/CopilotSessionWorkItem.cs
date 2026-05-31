namespace GHCP.Suite.Models;

public sealed class CopilotSessionWorkItem
{
    public string? Project { get; set; }
    public string Status { get; set; } = "Planned";
    public string Priority { get; set; } = "Normal";
    public string? NextAction { get; set; }
    public string? Decisions { get; set; }
    public string? Blockers { get; set; }
    public string? NextSteps { get; set; }
    public string? Outcome { get; set; }
    public List<CopilotSessionTaskItem> Tasks { get; set; } = [];
}
