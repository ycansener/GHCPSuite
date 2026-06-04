using GHCP.Suite.Components;
using GHCP.Suite.Extensions;
using GHCP.Suite.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddCopilotSuiteSettings(builder.Environment.ContentRootPath);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<CopilotSuiteOptions>(builder.Configuration.GetSection(CopilotSuiteOptions.SectionName));
builder.Services.AddSingleton<ICopilotSettingsService, CopilotSettingsService>();
builder.Services.AddSingleton<ICopilotEnvironmentService, CopilotEnvironmentService>();
builder.Services.AddSingleton<ICopilotWorkspaceAgentService, CopilotWorkspaceAgentService>();
builder.Services.AddSingleton<ICopilotAgentCatalogService, CopilotAgentCatalogService>();
builder.Services.AddSingleton<ICopilotAgentRunService, CopilotAgentRunService>();
builder.Services.AddSingleton<ICopilotWorkDataService, CopilotWorkDataService>();
builder.Services.AddSingleton<ICopilotWorkService, CopilotWorkService>();
builder.Services.AddSingleton<CopilotTickerService>();
builder.Services.AddSingleton<ICopilotTickerService>(static provider => provider.GetRequiredService<CopilotTickerService>());
builder.Services.AddHostedService(static provider => provider.GetRequiredService<CopilotTickerService>());
builder.Services.AddSingleton<ICopilotWorkspaceLaunchService, CopilotWorkspaceLaunchService>();
builder.Services.AddSingleton<ICopilotSessionService, CopilotSessionService>();
builder.Services.AddSingleton<ICopilotConfigService, CopilotConfigService>();
builder.Services.AddSingleton<ICopilotConfigEditorService, CopilotConfigEditorService>();
builder.Services.AddSingleton<ICopilotResumeService, CopilotResumeService>();
builder.Services.AddSingleton<ISuiteModuleProvider, DefaultSuiteModuleProvider>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var settingsService = scope.ServiceProvider.GetRequiredService<ICopilotSettingsService>();
    var workDataService = scope.ServiceProvider.GetRequiredService<ICopilotWorkDataService>();
    var workService = scope.ServiceProvider.GetRequiredService<ICopilotWorkService>();

    await settingsService.GetSettingsAsync();
    var suiteData = await workDataService.GetDataAsync();
    await workDataService.SaveDataAsync(suiteData);
    await workService.GetWorkspacesAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
