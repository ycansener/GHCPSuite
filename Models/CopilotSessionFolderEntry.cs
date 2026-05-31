namespace GHCP.Suite.Models;

public sealed record CopilotSessionFolderEntry(
    string Name,
    string RelativePath,
    string FullPath,
    bool IsDirectory,
    int Depth,
    DateTimeOffset UpdatedAt);
