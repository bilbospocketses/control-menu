using System.Reflection;
using ControlMenu.Data;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Modules.Utilities.Services;
using ControlMenu.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Prepend bundled dependency folders to PATH for self-contained operation
// ContentRootPath = project dir in dev, published root in production
var depsRoot = Path.Combine(builder.Environment.ContentRootPath, "dependencies");
if (Directory.Exists(depsRoot))
{
    var depPaths = Directory.GetDirectories(depsRoot)
        .Where(d => !Path.GetFileName(d).StartsWith('.'));
    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
    var newPath = string.Join(Path.PathSeparator, depPaths) + Path.PathSeparator + currentPath;
    Environment.SetEnvironmentVariable("PATH", newPath);
}
// Store deps root for modules to reference
builder.Configuration["DependenciesRoot"] = depsRoot;

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Database — factory pattern required for Blazor Server (avoids stale change-tracker state)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Data Protection (used by SecretStore for encrypting settings)
var keysPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ControlMenu", "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("ControlMenu");

// Core services
builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();
builder.Services.AddScoped<ISecretStore, SecretStore>();
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<INetworkDiscoveryService, NetworkDiscoveryService>();

// Android Devices module services
builder.Services.AddSingleton<IAdbService, AdbService>();

// ws-scrcpy-web process management
builder.Services.AddSingleton<WsScrcpyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WsScrcpyService>());

// Jellyfin module services
builder.Services.AddScoped<IJellyfinService, JellyfinService>();
builder.Services.AddScoped<IBackgroundJobService, BackgroundJobService>();

// Utilities module services
builder.Services.AddSingleton<IIconConversionService, IconConversionService>();
builder.Services.AddSingleton<IFileUnblockService, FileUnblockService>();

// Dependency management
builder.Services.AddHttpClient("github-api");
builder.Services.AddHttpClient("dependency-updates");
builder.Services.AddScoped<IDependencyManagerService>(sp =>
{
    var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    var modules = sp.GetRequiredService<ModuleDiscoveryService>().Modules;
    var executor = sp.GetRequiredService<ICommandExecutor>();
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var config = sp.GetRequiredService<IConfigurationService>();
    var wsScrcpy = sp.GetRequiredService<WsScrcpyService>();
    var logger = sp.GetRequiredService<ILogger<DependencyManagerService>>();
    return new DependencyManagerService(dbFactory, modules, executor, httpFactory, config, wsScrcpy, logger);
});
builder.Services.AddHostedService<DependencyCheckHostedService>();

// Module discovery — scans the main assembly for IToolModule implementations
builder.Services.AddSingleton(new ModuleDiscoveryService(
    [Assembly.GetExecutingAssembly()]));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<ControlMenu.Components.App>()
    .AddInteractiveServerRenderMode();

// Auto-apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var depManager = scope.ServiceProvider.GetRequiredService<IDependencyManagerService>();
    await depManager.SyncDependenciesAsync();
}

await app.RunAsync();
