namespace GHCP.Suite.Services;

public static class CopilotSuiteStorage
{
    public const string SuiteDirectoryName = ".ghcpsuite";
    public const string SettingsFileName = "customSettings.json";
    public const string DataFileName = "suiteData.json";

    public static string GetActiveUserProfile() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string GetSuiteHome() =>
        Path.Combine(GetActiveUserProfile(), SuiteDirectoryName);

    public static string GetSettingsFilePath(string suiteHome) =>
        Path.Combine(suiteHome, SettingsFileName);

    public static string GetDataFilePath(string suiteHome) =>
        Path.Combine(suiteHome, DataFileName);

    public static string GetLegacySettingsFilePath(string contentRootPath) =>
        Path.Combine(contentRootPath, SettingsFileName);

    public static string GetLegacyDataFilePath(string contentRootPath) =>
        Path.Combine(contentRootPath, DataFileName);

    public static void EnsureSuiteHome(string suiteHome)
    {
        if (string.IsNullOrWhiteSpace(suiteHome))
        {
            throw new ArgumentException("Suite home path is required.", nameof(suiteHome));
        }

        Directory.CreateDirectory(suiteHome);
    }
}
