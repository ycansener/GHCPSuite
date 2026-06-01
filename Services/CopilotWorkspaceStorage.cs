namespace GHCP.Suite.Services;

public static class CopilotWorkspaceStorage
{
    public const string SuiteDirectoryName = ".ghcpsuite";
    public const string LegacySuiteDirectoryName = ".ghcp-suite";
    public const string AgentsDirectoryName = "agents";
    public const string InitDirectoryName = "init";
    public const string TickersDirectoryName = "tickers";

    public static string GetSuiteRoot(string workspaceRootPath) =>
        Path.Combine(workspaceRootPath, SuiteDirectoryName);

    public static string GetLegacySuiteRoot(string workspaceRootPath) =>
        Path.Combine(workspaceRootPath, LegacySuiteDirectoryName);

    public static string GetAgentsRoot(string workspaceRootPath) =>
        Path.Combine(GetSuiteRoot(workspaceRootPath), AgentsDirectoryName);

    public static string GetInitRoot(string workspaceRootPath) =>
        Path.Combine(GetSuiteRoot(workspaceRootPath), InitDirectoryName);

    public static string GetTickersRoot(string workspaceRootPath) =>
        Path.Combine(GetSuiteRoot(workspaceRootPath), TickersDirectoryName);

    public static void EnsureWorkspaceStructure(string workspaceRootPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException("Workspace root path is required.", nameof(workspaceRootPath));
        }

        Directory.CreateDirectory(workspaceRootPath);

        var suiteRoot = GetSuiteRoot(workspaceRootPath);
        var legacyRoot = GetLegacySuiteRoot(workspaceRootPath);
        if (!Directory.Exists(suiteRoot) && Directory.Exists(legacyRoot))
        {
            Directory.Move(legacyRoot, suiteRoot);
        }

        Directory.CreateDirectory(suiteRoot);
        Directory.CreateDirectory(GetAgentsRoot(workspaceRootPath));
        Directory.CreateDirectory(GetInitRoot(workspaceRootPath));
        Directory.CreateDirectory(GetTickersRoot(workspaceRootPath));
    }
}
