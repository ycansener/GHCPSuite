namespace GHCP.Suite.Models;

public sealed record CopilotPaths(
    string UserProfile,
    string CopilotHome,
    string SessionStateDirectory,
    string SessionStoreDatabasePath,
    string ConfigPath,
    string SettingsPath,
    string CommandHistoryPath);
