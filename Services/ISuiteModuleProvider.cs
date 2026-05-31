using GHCP.Suite.Models;

namespace GHCP.Suite.Services;

public interface ISuiteModuleProvider
{
    IReadOnlyList<SuiteModuleDescriptor> GetModules();
}
