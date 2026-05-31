using GHCP.Suite.Models;

namespace GHCP.Suite.Services;

public interface ICopilotConfigService
{
    Task<IReadOnlyList<CopilotFileEntry>> GetConfigFilesAsync(CancellationToken cancellationToken = default);
}

public sealed class CopilotConfigService(ICopilotEnvironmentService environmentService) : ICopilotConfigService
{
    public async Task<IReadOnlyList<CopilotFileEntry>> GetConfigFilesAsync(CancellationToken cancellationToken = default)
    {
        var paths = environmentService.GetPaths();
        var knownFiles = new[]
        {
            ("config.json", paths.ConfigPath, "Global config"),
            ("settings.json", paths.SettingsPath, "User settings"),
            ("command-history-state.json", paths.CommandHistoryPath, "Command history state")
        };

        var results = new List<CopilotFileEntry>(knownFiles.Length);
        foreach (var (name, fullPath, category) in knownFiles)
        {
            results.Add(await GetFileAsync(name, fullPath, category, cancellationToken));
        }

        return results;
    }

    private static async Task<CopilotFileEntry> GetFileAsync(
        string name,
        string fullPath,
        string category,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            return new CopilotFileEntry(name, fullPath, category, false, null, null, "File not found.");
        }

        var info = new FileInfo(fullPath);
        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);

        return new CopilotFileEntry(
            name,
            fullPath,
            category,
            true,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc),
            content);
    }
}
