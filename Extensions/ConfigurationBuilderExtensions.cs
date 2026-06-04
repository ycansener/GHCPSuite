using Microsoft.Extensions.Configuration;
using GHCP.Suite.Services;

namespace GHCP.Suite.Extensions;

public static class ConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddCopilotSuiteSettings(this IConfigurationBuilder configurationBuilder, string contentRootPath)
    {
        var suiteHome = CopilotSuiteStorage.GetSuiteHome();
        CopilotSuiteStorage.EnsureSuiteHome(suiteHome);

        return configurationBuilder
            .AddJsonFile(CopilotSuiteStorage.GetSettingsFilePath(suiteHome), optional: true, reloadOnChange: true)
            .AddJsonFile(CopilotSuiteStorage.GetLegacySettingsFilePath(contentRootPath), optional: true, reloadOnChange: true);
    }
}
