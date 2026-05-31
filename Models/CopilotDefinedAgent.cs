namespace GHCP.Suite.Models;

public sealed record CopilotDefinedAgent(
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
    string FullPath);
