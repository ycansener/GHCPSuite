namespace GHCP.Suite.Models;

public sealed class SuiteDataDocument
{
    public List<CopilotWorkspace> Workspaces { get; set; } = [];
    public List<string> IgnoredWorkspaceFolders { get; set; } = [];
    public string? ActiveWorkspaceId { get; set; }
    public List<CopilotTickerDefinition> Tickers { get; set; } = [];
    public List<CopilotTickerRun> TickerRuns { get; set; } = [];
    public Dictionary<string, CopilotSessionWorkItem> SessionWork { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CopilotSavedView> SavedViews { get; set; } = [];
    public List<CopilotWorkTemplate> Templates { get; set; } = [];
    public List<CopilotActivityEntry> ActivityHistory { get; set; } = [];
}
