using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GHCP.Suite.Services;

public interface ICopilotSettingsService
{
    string GetSettingsFilePath();
    Task<CopilotSuiteOptions> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(CopilotSuiteOptions options, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetSessionCategoriesAsync(CancellationToken cancellationToken = default);
    Task SaveSessionCategoryAsync(string sessionId, string? category, CancellationToken cancellationToken = default);
}

public sealed class CopilotSettingsService(
    IHostEnvironment hostEnvironment,
    IOptionsMonitor<CopilotSuiteOptions> optionsMonitor,
    ICopilotEnvironmentService environmentService) : ICopilotSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string GetSettingsFilePath()
    {
        var paths = environmentService.GetPaths();
        CopilotSuiteStorage.EnsureSuiteHome(paths.SuiteHome);
        return paths.SuiteSettingsPath;
    }

    public async Task<CopilotSuiteOptions> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var filePath = GetSettingsFilePath();
        await MigrateLegacySettingsAsync(filePath, cancellationToken);
        if (!File.Exists(filePath))
        {
            return Clone(optionsMonitor.CurrentValue);
        }

        await using var stream = File.OpenRead(filePath);
        var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, SerializerOptions, cancellationToken);
        return Normalize(document?.CopilotSuite ?? new CopilotSuiteOptions());
    }

    public async Task SaveSettingsAsync(CopilotSuiteOptions options, CancellationToken cancellationToken = default)
    {
        var filePath = GetSettingsFilePath();
        await MigrateLegacySettingsAsync(filePath, cancellationToken);
        var normalized = Normalize(options);
        var document = new SettingsDocument
        {
            CopilotSuite = normalized
        };

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSessionCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        return new Dictionary<string, string>(settings.SessionCategories, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveSessionCategoryAsync(string sessionId, string? category, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        settings.SessionCategories ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(category))
        {
            settings.SessionCategories.Remove(sessionId);
        }
        else
        {
            settings.SessionCategories[sessionId] = category.Trim();
        }

        await SaveSettingsAsync(settings, cancellationToken);
    }

    private static CopilotSuiteOptions Clone(CopilotSuiteOptions source) => new()
    {
        WorkspaceRootDirectory = source.WorkspaceRootDirectory,
        StartupDirectory = source.StartupDirectory,
        UserProfile = source.UserProfile,
        CopilotHome = source.CopilotHome,
        SessionStateDirectory = source.SessionStateDirectory,
        SessionStoreDatabasePath = source.SessionStoreDatabasePath,
        ConfigPath = source.ConfigPath,
        SettingsPath = source.SettingsPath,
        CommandHistoryPath = source.CommandHistoryPath,
        PreferWindowsTerminal = source.PreferWindowsTerminal,
        SessionCategories = new Dictionary<string, string>(source.SessionCategories, StringComparer.OrdinalIgnoreCase)
    };

    private static CopilotSuiteOptions Normalize(CopilotSuiteOptions source) => new()
    {
        WorkspaceRootDirectory = NullIfBlank(source.WorkspaceRootDirectory),
        StartupDirectory = NullIfBlank(source.StartupDirectory),
        UserProfile = NullIfBlank(source.UserProfile),
        CopilotHome = NullIfBlank(source.CopilotHome),
        SessionStateDirectory = NullIfBlank(source.SessionStateDirectory),
        SessionStoreDatabasePath = NullIfBlank(source.SessionStoreDatabasePath),
        ConfigPath = NullIfBlank(source.ConfigPath),
        SettingsPath = NullIfBlank(source.SettingsPath),
        CommandHistoryPath = NullIfBlank(source.CommandHistoryPath),
        PreferWindowsTerminal = source.PreferWindowsTerminal,
        SessionCategories = NormalizeCategoryMap(source.SessionCategories)
    };

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string> NormalizeCategoryMap(Dictionary<string, string>? source)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return normalized;
        }

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[key.Trim()] = value.Trim();
        }

        return normalized;
    }

    private sealed class SettingsDocument
    {
        public CopilotSuiteOptions CopilotSuite { get; set; } = new();
    }

    private async Task MigrateLegacySettingsAsync(string targetPath, CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        var legacyPath = CopilotSuiteStorage.GetLegacySettingsFilePath(hostEnvironment.ContentRootPath);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        await using var sourceStream = File.Open(legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destinationStream = File.Create(targetPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }
}
