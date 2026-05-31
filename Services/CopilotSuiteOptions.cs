namespace GHCP.Suite.Services;

public sealed class CopilotSuiteOptions
{
    public const string SectionName = "CopilotSuite";

    public string? WorkspaceRootDirectory { get; set; }
    public string? StartupDirectory { get; set; }
    public string? UserProfile { get; set; }
    public string? CopilotHome { get; set; }
    public string? SessionStateDirectory { get; set; }
    public string? SessionStoreDatabasePath { get; set; }
    public string? ConfigPath { get; set; }
    public string? SettingsPath { get; set; }
    public string? CommandHistoryPath { get; set; }
    public bool PreferWindowsTerminal { get; set; } = true;
    public Dictionary<string, string> SessionCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
