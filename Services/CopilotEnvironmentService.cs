using GHCP.Suite.Models;
using Microsoft.Extensions.Options;

namespace GHCP.Suite.Services;

public interface ICopilotEnvironmentService
{
    CopilotPaths GetPaths();
}

public sealed class CopilotEnvironmentService(IOptionsMonitor<CopilotSuiteOptions> options) : ICopilotEnvironmentService
{
    public CopilotPaths GetPaths() => BuildPaths(options.CurrentValue);

    private static CopilotPaths BuildPaths(CopilotSuiteOptions options)
    {
        var activeUserProfile = CopilotSuiteStorage.GetActiveUserProfile();
        var userProfile = FirstNonEmpty(options.UserProfile, activeUserProfile);
        var suiteHome = CopilotSuiteStorage.GetSuiteHome();
        var copilotHome = FirstNonEmpty(options.CopilotHome, Path.Combine(userProfile, ".copilot"));

        return new CopilotPaths(
            userProfile,
            suiteHome,
            CopilotSuiteStorage.GetSettingsFilePath(suiteHome),
            CopilotSuiteStorage.GetDataFilePath(suiteHome),
            copilotHome,
            FirstNonEmpty(options.SessionStateDirectory, Path.Combine(copilotHome, "session-state")),
            FirstNonEmpty(options.SessionStoreDatabasePath, Path.Combine(copilotHome, "session-store.db")),
            FirstNonEmpty(options.ConfigPath, Path.Combine(copilotHome, "config.json")),
            FirstNonEmpty(options.SettingsPath, Path.Combine(copilotHome, "settings.json")),
            FirstNonEmpty(options.CommandHistoryPath, Path.Combine(copilotHome, "command-history-state.json")));
    }

    private static string FirstNonEmpty(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}
