namespace GHCP.Suite.Models;

public sealed record CopilotFileEntry(
    string Name,
    string FullPath,
    string Category,
    bool Exists,
    long? SizeBytes,
    DateTimeOffset? LastModified,
    string Content);
