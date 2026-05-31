namespace GHCP.Suite.Models;

public sealed record SuiteModuleDescriptor(
    string Id,
    string Title,
    string Route,
    string Description,
    string Glyph,
    int Order);
