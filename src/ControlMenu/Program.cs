using System.Reflection;
using ControlMenu.Data;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Modules.Utilities.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// ContentRootPath = project dir in dev, published root in production
var depsRoot = Path.Combine(builder.Environment.ContentRootPath, "dependencies");
builder.Configuration["DependenciesRoot"] = depsRoot;

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Icon Converter ships image bytes (base64) over the SignalR circuit;
        // default 32KB cap rejects anything but a tiny image.
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024; // 32 MB
    });

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
builder.Services.AddScoped<IDependencyPathResolver>(sp =>
    new DependencyPathResolver(
        sp.GetRequiredService<ModuleDiscoveryService>().Modules,
        sp.GetRequiredService<IConfigurationService>()));
builder.Services.AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IDeviceTypeCache, DeviceTypeCache>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<INetworkDiscoveryService, NetworkDiscoveryService>();

builder.Services.AddSingleton<IScrcpyProbeService, ScrcpyProbeService>();

// Android Devices module services
builder.Services.AddScoped<IAdbService, AdbService>();

// ws-scrcpy-web process management
builder.Services.AddSingleton<WsScrcpyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WsScrcpyService>());

// Network scanner — singleton holds the ws-scan WebSocket and fans events to subscribers
builder.Services.AddSingleton<INetworkScanService, NetworkScanService>();
builder.Services.AddScoped<IScanLifecycleHandler, ScanLifecycleHandler>();
builder.Services.AddScoped<SubnetDetectionClient>();

// Jellyfin module services
builder.Services.AddScoped<IJellyfinService, JellyfinService>();
builder.Services.AddScoped<IBackgroundJobService, BackgroundJobService>();

// Utilities module services
builder.Services.AddSingleton<IIconConversionService, IconConversionService>();
builder.Services.AddSingleton<IFileUnblockService, FileUnblockService>();

// Cameras module services
builder.Services.AddSingleton<ICameraChangeNotifier, CameraChangeNotifier>();
builder.Services.AddScoped<ICameraService, CameraService>();

// go2rtc streaming service
builder.Services.AddSingleton<IGo2RtcService, Go2RtcService>();
builder.Services.AddHostedService(sp => (Go2RtcService)sp.GetRequiredService<IGo2RtcService>());

// Dependency management
builder.Services.AddHttpClient();
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
    var go2Rtc = sp.GetRequiredService<IGo2RtcService>();
    var resolver = sp.GetRequiredService<IDependencyPathResolver>();
    var logger = sp.GetRequiredService<ILogger<DependencyManagerService>>();
    return new DependencyManagerService(dbFactory, modules, executor, httpFactory, config, wsScrcpy, go2Rtc, resolver, logger);
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

    // Normalize any MAC addresses stored with colons or mixed case
    var devicesWithBadMac = db.Devices
        .AsEnumerable()
        .Where(d => d.MacAddress != NetworkDiscoveryService.NormalizeMac(d.MacAddress))
        .ToList();
    foreach (var device in devicesWithBadMac)
        device.MacAddress = NetworkDiscoveryService.NormalizeMac(device.MacAddress);
    if (devicesWithBadMac.Count > 0)
        db.SaveChanges();


    var depManager = scope.ServiceProvider.GetRequiredService<IDependencyManagerService>();
    await depManager.SyncDependenciesAsync();

    // Load camera names for sidebar nav entries (module can't do async)
    var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();
    var allCameras = await cameraService.GetAllAsync();
    CamerasModule.EnabledCameras = allCameras
        .Select(c => (c.Id, c.Name))
        .ToList();
}

// Refresh sidebar nav when cameras change (Add/Update/Delete)
var cameraNotifier = app.Services.GetRequiredService<ICameraChangeNotifier>();
cameraNotifier.CamerasChanged += () =>
{
    using var scope = app.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<ICameraService>();
    var fresh = svc.GetAllAsync().GetAwaiter().GetResult();
    CamerasModule.EnabledCameras = fresh.Select(c => (c.Id, c.Name)).ToList();
};

await app.RunAsync();
