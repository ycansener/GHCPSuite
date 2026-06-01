using System.Globalization;
using GHCP.Suite.Models;
using Microsoft.Extensions.Options;

namespace GHCP.Suite.Services;

public interface ICopilotAgentCatalogService
{
    Task<IReadOnlyList<CopilotDefinedAgent>> GetDefinedAgentsAsync(CancellationToken cancellationToken = default);
    Task<CopilotDefinedAgent?> GetDefinedAgentAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class CopilotAgentCatalogService(
    ICopilotEnvironmentService environmentService,
    ICopilotWorkspaceAgentService workspaceAgentService,
    ICopilotWorkService workService,
    IOptionsMonitor<CopilotSuiteOptions> optionsMonitor) : ICopilotAgentCatalogService
{
    public async Task<IReadOnlyList<CopilotDefinedAgent>> GetDefinedAgentsAsync(CancellationToken cancellationToken = default)
    {
        var paths = environmentService.GetPaths();
        var agents = new List<CopilotDefinedAgent>();
        var customAgents = new Dictionary<string, CopilotDefinedAgent>(StringComparer.OrdinalIgnoreCase);
        var workspaceScopedAgents = new List<CopilotDefinedAgent>();

        foreach (var customRoot in GetCustomDefinitions(optionsMonitor.CurrentValue, paths))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var agent in await LoadCustomAgentsAsync(customRoot.Path, customRoot.SourceLabel, GetExcludedMirrorRoot(paths), cancellationToken))
            {
                customAgents[agent.Name] = agent;
            }
        }

        agents.AddRange(customAgents.Values);

        foreach (var workspaceAgent in await workspaceAgentService.GetWorkspaceAgentsAsync(cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workspace = await workService.GetWorkspaceAsync(workspaceAgent.WorkspaceId, cancellationToken);
            if (workspace is null || string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                continue;
            }

            var agentsRoot = CopilotWorkspaceStorage.GetAgentsRoot(workspace.RootPath);
            var filePath = Path.Combine(agentsRoot, workspaceAgent.FileName);
            if (!File.Exists(filePath))
            {
                continue;
            }

            var agent = await ParseDefinitionAsync(
                filePath,
                agentsRoot,
                packageVersion: "Workspace clone",
                isCustom: true,
                sourceLabel: $"Workspace · {workspace.Name}",
                cancellationToken,
                idOverride: workspaceAgent.Id,
                workspaceId: workspace.Id,
                workspaceName: workspace.Name,
                enabled: workspaceAgent.Enabled,
                workspaceAgentId: workspaceAgent.Id);

            if (agent is not null)
            {
                workspaceScopedAgents.Add(agent);
            }
        }

        agents.AddRange(workspaceScopedAgents);

        var definitionsRoot = GetDefinitionsRoot(paths.CopilotHome);
        if (definitionsRoot is not null && Directory.Exists(definitionsRoot))
        {
            var packageVersion = Directory.GetParent(definitionsRoot)?.Name ?? "Unknown";
            var files = EnumerateBuiltInDefinitionFiles(definitionsRoot).ToArray();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var agent = await ParseDefinitionAsync(
                    file,
                    definitionsRoot,
                    packageVersion,
                    isCustom: false,
                    sourceLabel: "Built-in",
                    cancellationToken);
                if (agent is not null)
                {
                    agents.Add(agent);
                }
            }
        }

        return agents
            .OrderByDescending(agent => agent.IsCustom)
            .ThenByDescending(agent => agent.IsWorkspaceScoped)
            .ThenBy(agent => agent.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(agent => agent.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<CopilotDefinedAgent?> GetDefinedAgentAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var agents = await GetDefinedAgentsAsync(cancellationToken);
        return agents.FirstOrDefault(agent =>
            string.Equals(agent.Id, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(agent.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<(string Path, string SourceLabel)> GetCustomDefinitions(CopilotSuiteOptions options, CopilotPaths paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.StartupDirectory))
        {
            var projectAgentsPath = Path.Combine(options.StartupDirectory, ".github", "agents");
            if (Directory.Exists(projectAgentsPath) && seen.Add(projectAgentsPath))
            {
                yield return (projectAgentsPath, "Project custom");
            }
        }

        var userAgentsPath = Path.Combine(paths.CopilotHome, "agents");
        if (Directory.Exists(userAgentsPath) && seen.Add(userAgentsPath))
        {
            yield return (userAgentsPath, "User custom");
        }
    }

    private static string? GetDefinitionsRoot(string copilotHome)
    {
        var packageRoots = new[]
        {
            Path.Combine(copilotHome, "pkg", "universal"),
            Path.Combine(copilotHome, "pkg", "win32-x64")
        };

        foreach (var packageRoot in packageRoots)
        {
            if (!Directory.Exists(packageRoot))
            {
                continue;
            }

            var latest = Directory.EnumerateDirectories(packageRoot)
                .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
                .Where(entry => entry.Version is not null && Directory.Exists(Path.Combine(entry.Path, "definitions")))
                .OrderByDescending(entry => entry.Version)
                .FirstOrDefault();

            if (latest is not null)
            {
                return Path.Combine(latest.Path, "definitions");
            }
        }

        return null;
    }

    private static Version? ParseVersion(string? value) =>
        Version.TryParse(value, out var version) ? version : null;

    private static IEnumerable<string> EnumerateBuiltInDefinitionFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateCustomDefinitionFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.agent.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<CopilotDefinedAgent>> LoadCustomAgentsAsync(
        string root,
        string sourceLabel,
        string? excludedRoot,
        CancellationToken cancellationToken)
    {
        var agents = new Dictionary<string, CopilotDefinedAgent>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateCustomDefinitionFiles(root))
        {
            if (!string.IsNullOrWhiteSpace(excludedRoot) && IsUnderPath(file, excludedRoot))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var agent = await ParseDefinitionAsync(
                file,
                root,
                packageVersion: "Custom",
                isCustom: true,
                sourceLabel,
                cancellationToken);
            if (agent is not null)
            {
                agents[agent.Name] = agent;
            }
        }

        return agents.Values.ToArray();
    }

    private static async Task<CopilotDefinedAgent?> ParseDefinitionAsync(
        string filePath,
        string definitionsRoot,
        string packageVersion,
        bool isCustom,
        string sourceLabel,
        CancellationToken cancellationToken,
        string? idOverride = null,
        string? workspaceId = null,
        string? workspaceName = null,
        bool enabled = true,
        string? workspaceAgentId = null)
    {
        var lines = await LoadDefinitionLinesAsync(filePath, cancellationToken);

        string? name = null;
        string? displayName = null;
        string? description = null;
        string? model = null;
        List<string>? tools = null;
        var kind = filePath.Contains($"{Path.DirectorySeparatorChar}sidekick{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Sidekick"
            : "Agent";

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var indent = GetIndent(line);
            if (indent != 0)
            {
                continue;
            }

            if (TryGetScalar(trimmed, "name", out var parsedName))
            {
                name = parsedName;
                continue;
            }

            if (TryGetScalar(trimmed, "displayName", out var parsedDisplayName))
            {
                displayName = parsedDisplayName;
                continue;
            }

            if (trimmed.StartsWith("description:", StringComparison.Ordinal))
            {
                description = ParseBlockOrScalar(lines, ref index, "description");
                continue;
            }

            if (trimmed.StartsWith("model:", StringComparison.Ordinal))
            {
                model = ParseListOrScalar(lines, ref index, "model");
                continue;
            }

            if (trimmed.StartsWith("tools:", StringComparison.Ordinal))
            {
                tools = ParseList(lines, ref index);
                continue;
            }

            if (trimmed.StartsWith("sidekick:", StringComparison.Ordinal))
            {
                kind = "Sidekick";
            }
        }

        var invocationName = isCustom && filePath.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase)
            ? GetCustomAgentInvocationName(filePath)
            : null;

        if (isCustom && (string.IsNullOrWhiteSpace(invocationName) || string.IsNullOrWhiteSpace(description)))
        {
            return null;
        }

        var configuredName = name;
        name = invocationName ?? configuredName ?? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath));
        displayName ??= isCustom ? configuredName ?? name : name;
        description ??= "No description found in the Copilot definition.";
        var relativePath = Path.GetRelativePath(definitionsRoot, filePath);

        return new CopilotDefinedAgent(
            idOverride ?? CreateAgentId(isCustom, filePath),
            name,
            displayName,
            description,
            kind,
            string.IsNullOrWhiteSpace(model) ? "Dynamic / unspecified" : model,
            FormatToolAccess(tools),
            isCustom,
            sourceLabel,
            packageVersion,
            relativePath,
            filePath,
            workspaceId,
            workspaceName,
            enabled,
            workspaceAgentId);
    }

    private static string GetCustomAgentInvocationName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".agent.md".Length]
            : Path.GetFileNameWithoutExtension(filePath);
    }

    private static async Task<string[]> LoadDefinitionLinesAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        if (!filePath.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase))
        {
            return lines;
        }

        if (lines.Length == 0 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.Equals(lines[index].Trim(), "---", StringComparison.Ordinal))
            {
                return lines[1..index];
            }
        }

        return Array.Empty<string>();
    }

    private static string FormatToolAccess(List<string>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return "No tools listed";
        }

        if (tools.Count == 1 && string.Equals(tools[0], "*", StringComparison.Ordinal))
        {
            return "All tools";
        }

        return tools.Count == 1 ? tools[0] : $"{tools.Count} tools";
    }

    private static bool TryGetScalar(string trimmedLine, string key, out string? value)
    {
        var prefix = key + ":";
        if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        var rawValue = trimmedLine[prefix.Length..].Trim();
        value = Unquote(rawValue);
        return true;
    }

    private static string ParseBlockOrScalar(string[] lines, ref int index, string key)
    {
        var prefix = key + ":";
        var line = lines[index].Trim();
        var rawValue = line[prefix.Length..].Trim();

        if (!string.IsNullOrWhiteSpace(rawValue) && rawValue is not ">" and not "|")
        {
            return Unquote(rawValue);
        }

        var blockLines = new List<string>();
        var baseIndent = GetIndent(lines[index]);

        for (var next = index + 1; next < lines.Length; next++)
        {
            var candidate = lines[next];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                blockLines.Add(string.Empty);
                index = next;
                continue;
            }

            var indent = GetIndent(candidate);
            if (indent <= baseIndent)
            {
                break;
            }

            blockLines.Add(candidate.Trim());
            index = next;
        }

        return string.Join(" ", blockLines.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    private static string? ParseListOrScalar(string[] lines, ref int index, string key)
    {
        var prefix = key + ":";
        var line = lines[index].Trim();
        var rawValue = line[prefix.Length..].Trim();

        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            return Unquote(rawValue);
        }

        var items = ParseList(lines, ref index);
        return items.Count == 0 ? null : string.Join(", ", items);
    }

    private static List<string> ParseList(string[] lines, ref int index)
    {
        var items = new List<string>();
        var baseIndent = GetIndent(lines[index]);

        for (var next = index + 1; next < lines.Length; next++)
        {
            var candidate = lines[next];
            var trimmed = candidate.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                index = next;
                continue;
            }

            var indent = GetIndent(candidate);
            if (indent <= baseIndent)
            {
                break;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                items.Add(Unquote(trimmed[2..].Trim()));
            }

            index = next;
        }

        return items;
    }

    private static int GetIndent(string value)
    {
        var count = 0;
        while (count < value.Length && char.IsWhiteSpace(value[count]))
        {
            count++;
        }

        return count;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string CreateAgentId(bool isCustom, string filePath)
    {
        var prefix = isCustom ? "custom" : "builtin";
        return $"{prefix}:{filePath}".ToLowerInvariant();
    }

    private static string GetExcludedMirrorRoot(CopilotPaths paths) =>
        CopilotWorkspaceAgentService.GetMirrorCollectionRoot(paths.CopilotHome);

    private static bool IsUnderPath(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
