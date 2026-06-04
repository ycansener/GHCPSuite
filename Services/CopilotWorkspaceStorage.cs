namespace GHCP.Suite.Services;

public static class CopilotWorkspaceStorage
{
    public const string LegacySuiteDirectoryName = ".ghcp-suite";
    public const string AgentsDirectoryName = "agents";
    public const string InitDirectoryName = "init";
    public const string TickersDirectoryName = "tickers";

    public static string GetWorkspaceDataRoot(string workspaceDataPath) =>
        NormalizePath(workspaceDataPath);

    public static string GetAgentsRoot(string workspaceDataPath) =>
        Path.Combine(GetWorkspaceDataRoot(workspaceDataPath), AgentsDirectoryName);

    public static string GetInitRoot(string workspaceDataPath) =>
        Path.Combine(GetWorkspaceDataRoot(workspaceDataPath), InitDirectoryName);

    public static string GetTickersRoot(string workspaceDataPath) =>
        Path.Combine(GetWorkspaceDataRoot(workspaceDataPath), TickersDirectoryName);

    public static IReadOnlyList<string> GetLegacyWorkspaceSuiteRoots(string workspaceRootPath)
    {
        var normalizedRoot = NormalizePath(workspaceRootPath);
        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            return Array.Empty<string>();
        }

        return
        [
            Path.Combine(normalizedRoot, CopilotSuiteStorage.SuiteDirectoryName),
            Path.Combine(normalizedRoot, LegacySuiteDirectoryName)
        ];
    }

    public static void EnsureWorkspaceDataStructure(string workspaceDataPath, string? workspaceRootPath = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceDataPath))
        {
            throw new ArgumentException("Workspace data path is required.", nameof(workspaceDataPath));
        }

        var dataRoot = GetWorkspaceDataRoot(workspaceDataPath);
        Directory.CreateDirectory(dataRoot);

        if (!string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            foreach (var legacyRoot in GetLegacyWorkspaceSuiteRoots(workspaceRootPath))
            {
                if (!Directory.Exists(legacyRoot) || PathsEqual(legacyRoot, dataRoot))
                {
                    continue;
                }

                MergeDirectory(legacyRoot, dataRoot);
                DeleteDirectoryIfEmpty(legacyRoot);
            }
        }

        Directory.CreateDirectory(GetAgentsRoot(dataRoot));
        Directory.CreateDirectory(GetInitRoot(dataRoot));
        Directory.CreateDirectory(GetTickersRoot(dataRoot));
    }

    private static void MergeDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            if (Directory.Exists(destinationPath))
            {
                MergeDirectory(directory, destinationPath);
            }
            else
            {
                Directory.Move(directory, destinationPath);
            }
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(file));
            if (File.Exists(destinationPath))
            {
                File.Delete(file);
                continue;
            }

            File.Move(file, destinationPath);
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            return;
        }

        Directory.Delete(path, recursive: false);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
        catch (NotSupportedException)
        {
            return trimmed;
        }
        catch (PathTooLongException)
        {
            return trimmed;
        }
    }
}
