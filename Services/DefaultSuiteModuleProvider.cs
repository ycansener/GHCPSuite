using GHCP.Suite.Models;

namespace GHCP.Suite.Services;

public sealed class DefaultSuiteModuleProvider : ISuiteModuleProvider
{
    private static readonly SuiteModuleDescriptor[] Modules =
    [
        new("workspaces", "Workspaces", "/", "Workspace-first entry point for scoped Copilot work.", "[O]", 0),
        new("agents", "Agents", "/agents", "Installed Copilot agent definitions from the local CLI package.", "[A]", 1),
        new("tickers", "Tickers", "/tickers", "Recurring GHCP or agent prompts that run on an interval.", "[K]", 2),
        new("sessions", "Sessions", "/sessions", "Previous and current GHCP sessions across all workspaces.", "[S]", 3),
        new("work", "Work", "/work", "Global workflow dashboard with search, templates, views, and timeline.", "[W]", 4),
        new("dashboards", "Dashboards", "/dashboards", "Workspace telemetry and usage dashboards.", "[D]", 5),
        new("config", "Config", "/config", "Global GHCP configuration files.", "[C]", 6),
        new("extensions", "Extensions", "/extensions", "Registered modules and extension surface.", "[X]", 7),
        new("settings", "Settings", "/settings", "Editable suite settings loaded from customSettings.json.", "[T]", 8)
    ];

    public IReadOnlyList<SuiteModuleDescriptor> GetModules() => Modules;
}
