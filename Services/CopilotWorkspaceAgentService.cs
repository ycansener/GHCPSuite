using GHCP.Suite.Models;

namespace GHCP.Suite.Services;

public interface ICopilotWorkspaceAgentService
{
    Task<IReadOnlyList<CopilotWorkspaceAgent>> GetWorkspaceAgentsAsync(string? workspaceId = null, CancellationToken cancellationToken = default);
    Task<CopilotWorkspaceAgent?> GetWorkspaceAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task<CopilotWorkspaceAgent?> GetWorkspaceAgentByInvocationNameAsync(string workspaceId, string agentName, CancellationToken cancellationToken = default);
    Task<CopilotWorkspaceAgent> CloneAgentToWorkspaceAsync(CopilotDefinedAgent source, string workspaceId, CancellationToken cancellationToken = default);
    Task SetWorkspaceAgentEnabledAsync(string agentId, bool enabled, CancellationToken cancellationToken = default);
    Task<string?> SynchronizeWorkspaceAgentAsync(string agentId, CancellationToken cancellationToken = default);
}

public sealed class CopilotWorkspaceAgentService(
    ICopilotWorkDataService workDataService,
    ICopilotWorkService workService,
    ICopilotEnvironmentService environmentService) : ICopilotWorkspaceAgentService
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;
    private const string MirrorRootFolderName = "ghcpsuite";

    public async Task<IReadOnlyList<CopilotWorkspaceAgent>> GetWorkspaceAgentsAsync(string? workspaceId = null, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.WorkspaceAgents
            .Where(agent => string.IsNullOrWhiteSpace(workspaceId) || TextComparer.Equals(agent.WorkspaceId, workspaceId))
            .Select(CloneAgent)
            .OrderBy(agent => agent.DisplayName, TextComparer)
            .ThenBy(agent => agent.Name, TextComparer)
            .ToArray();
    }

    public async Task<CopilotWorkspaceAgent?> GetWorkspaceAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return null;
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.WorkspaceAgents
            .Where(agent => TextComparer.Equals(agent.Id, agentId))
            .Select(CloneAgent)
            .FirstOrDefault();
    }

    public async Task<CopilotWorkspaceAgent?> GetWorkspaceAgentByInvocationNameAsync(string workspaceId, string agentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(agentName))
        {
            return null;
        }

        var data = await workDataService.GetDataAsync(cancellationToken);
        return data.WorkspaceAgents
            .Where(agent =>
                TextComparer.Equals(agent.WorkspaceId, workspaceId) &&
                TextComparer.Equals(agent.Name, agentName))
            .Select(CloneAgent)
            .FirstOrDefault();
    }

    public async Task<CopilotWorkspaceAgent> CloneAgentToWorkspaceAsync(CopilotDefinedAgent source, string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        if (string.IsNullOrWhiteSpace(source.FullPath) || !File.Exists(source.FullPath))
        {
            throw new FileNotFoundException("The source agent definition could not be found.", source.FullPath);
        }

        var workspace = await workService.GetWorkspaceAsync(workspaceId, cancellationToken)
            ?? throw new InvalidOperationException("The destination workspace could not be found.");

        CopilotWorkspaceStorage.EnsureWorkspaceStructure(workspace.RootPath);

        var data = await workDataService.GetDataAsync(cancellationToken);
        var agentsRoot = CopilotWorkspaceStorage.GetAgentsRoot(workspace.RootPath);
        Directory.CreateDirectory(agentsRoot);

        var baseName = BuildBaseInvocationName(workspace.Name, source.Name);
        var invocationName = GetUniqueInvocationName(baseName, agentsRoot, data.WorkspaceAgents.Where(agent => TextComparer.Equals(agent.WorkspaceId, workspace.Id)));
        var fileName = $"{invocationName}.agent.md";
        var destinationPath = Path.Combine(agentsRoot, fileName);

        await using (var sourceStream = File.OpenRead(source.FullPath))
        await using (var destinationStream = File.Create(destinationPath))
        {
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var clone = new CopilotWorkspaceAgent
        {
            Id = Guid.NewGuid().ToString("N"),
            WorkspaceId = workspace.Id,
            Name = invocationName,
            DisplayName = source.DisplayName,
            FileName = fileName,
            SourceAgentId = source.Id,
            SourceAgentName = source.Name,
            SourceLabel = source.SourceLabel,
            Enabled = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        data.WorkspaceAgents.Add(clone);
        await workDataService.SaveDataAsync(data, cancellationToken);
        await SynchronizeWorkspaceAgentAsync(clone.Id, cancellationToken);
        return CloneAgent(clone);
    }

    public async Task SetWorkspaceAgentEnabledAsync(string agentId, bool enabled, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var agent = data.WorkspaceAgents.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, agentId));
        if (agent is null)
        {
            return;
        }

        agent.Enabled = enabled;
        agent.UpdatedAt = DateTimeOffset.UtcNow;
        await workDataService.SaveDataAsync(data, cancellationToken);
    }

    public async Task<string?> SynchronizeWorkspaceAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var data = await workDataService.GetDataAsync(cancellationToken);
        var agent = data.WorkspaceAgents.FirstOrDefault(candidate => TextComparer.Equals(candidate.Id, agentId));
        if (agent is null)
        {
            return null;
        }

        var workspace = await workService.GetWorkspaceAsync(agent.WorkspaceId, cancellationToken);
        if (workspace is null || string.IsNullOrWhiteSpace(workspace.RootPath))
        {
            return null;
        }

        CopilotWorkspaceStorage.EnsureWorkspaceStructure(workspace.RootPath);
        var sourcePath = Path.Combine(CopilotWorkspaceStorage.GetAgentsRoot(workspace.RootPath), agent.FileName);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var mirrorPath = GetMirrorPath(environmentService.GetPaths().CopilotHome, workspace.Id, agent.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(mirrorPath)!);

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(mirrorPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        return mirrorPath;
    }

    public static string GetMirrorCollectionRoot(string copilotHome) =>
        Path.Combine(copilotHome, "agents", MirrorRootFolderName);

    private static CopilotWorkspaceAgent CloneAgent(CopilotWorkspaceAgent source) => new()
    {
        Id = source.Id,
        WorkspaceId = source.WorkspaceId,
        Name = source.Name,
        DisplayName = source.DisplayName,
        FileName = source.FileName,
        SourceAgentId = source.SourceAgentId,
        SourceAgentName = source.SourceAgentName,
        SourceLabel = source.SourceLabel,
        Enabled = source.Enabled,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static string GetUniqueInvocationName(string baseName, string agentsRoot, IEnumerable<CopilotWorkspaceAgent> existingAgents)
    {
        var existingNames = new HashSet<string>(
            existingAgents.Select(agent => agent.Name),
            TextComparer);

        var candidate = baseName;
        var suffix = 2;
        while (existingNames.Contains(candidate) || File.Exists(Path.Combine(agentsRoot, $"{candidate}.agent.md")))
        {
            candidate = $"{baseName}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string BuildBaseInvocationName(string workspaceName, string sourceName)
    {
        var workspaceSlug = Slugify(workspaceName, 24);
        var sourceSlug = Slugify(sourceName, 32);
        return $"{workspaceSlug}-{sourceSlug}";
    }

    private static string Slugify(string value, int maxLength)
    {
        var collapsed = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (collapsed.Contains("--", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        }

        collapsed = collapsed.Trim('-');
        if (collapsed.Length > maxLength)
        {
            collapsed = collapsed[..maxLength].Trim('-');
        }

        return string.IsNullOrWhiteSpace(collapsed) ? "agent" : collapsed;
    }

    private static string GetMirrorPath(string copilotHome, string workspaceId, string agentName) =>
        Path.Combine(GetMirrorCollectionRoot(copilotHome), workspaceId, $"{agentName}.agent.md");
}
