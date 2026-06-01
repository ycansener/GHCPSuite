namespace GHCP.Suite.Models;

public sealed record CopilotDefinedAgent(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    string Kind,
    string Model,
    string ToolAccess,
    bool IsCustom,
    string SourceLabel,
    string PackageVersion,
    string DefinitionPath,
    string FullPath,
    string? WorkspaceId = null,
    string? WorkspaceName = null,
    bool Enabled = true,
    string? WorkspaceAgentId = null)
{
    public bool IsWorkspaceScoped => !string.IsNullOrWhiteSpace(WorkspaceId);
}
