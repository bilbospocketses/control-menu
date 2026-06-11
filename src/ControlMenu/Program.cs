using System.Reflection;
using ControlMenu.Data;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices.Services;
using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Cameras.Migrations;
using ControlMenu.Modules.Cameras.Network;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Modules.Utilities.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
using ControlMenu.Services.Startup;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// VelopackApp.Build().Run() must be called in every process that consumes
// Velopack APIs — VelopackLocator.Current is a per-process static and is
// NOT inherited across the ControlMenuLauncher → ControlMenu.exe boundary.
// Without this, UpdateManager's ctor throws "No VelopackLocator has been set"
// when the DI container instantiates VelopackUpdateService (e.g. on
// Settings → General page load), tearing down the Blazor circuit.
// SetAutoApplyOnStartup(false): the launcher owns apply orchestration via
// exit code 75; the Blazor host must never auto-apply on its own startup.
Velopack.VelopackApp.Build()
    .SetAutoApplyOnStartup(false)
    .Run();

var builder = WebApplication.CreateBuilder(args);

// Resolve all writable-state paths through IDataPathResolver. Velopack mode
// roots at C:\ProgramData\ControlMenu; dev mode roots at AppContext.BaseDirectory.
// Selector probes for ..\..\Update.exe — present in Velopack installs.
var dataPathResolver = ControlMenu.Common.Paths.DataPathResolverFactory.CreateFromCurrentProcess();
Directory.CreateDirectory(dataPathResolver.GetConfigDir());
Directory.CreateDirectory(dataPathResolver.GetLogsDir());
Directory.CreateDirectory(dataPathResolver.GetKeysDir());
Directory.CreateDirectory(dataPathResolver.GetDependenciesDir());

// Bind Kestrel to the port configured in app-config.json (default 5159).
// Properties/launchSettings.json only applies to `dotnet run`; published
// builds need an explicit Urls binding or ASP.NET falls back to :5000.
var appConfig = ControlMenu.Common.Config.AppConfig.Load(dataPathResolver.GetAppConfigPath());
builder.WebHost.UseUrls(ControlMenu.Common.Config.WebPortResolver.GetKestrelUrl(appConfig));

// File logging — published builds lose stdout when the launcher detaches the
// console, so controlmenu.log under <dataRoot>/logs/ is the only post-mortem
// trail we have. Default ASP.NET console-only setup leaves no file behind.
builder.Logging.ClearProviders();
ControlMenu.Logging.FileLoggingConfigurator.AddFileSink(
    builder.Logging,
    Path.Combine(dataPathResolver.GetLogsDir(), "controlmenu.log"));

// DepsRootHolder is read by static module-init for AndroidDevicesModule,
// CamerasModule, JellyfinModule. Must be set before module discovery runs.
ControlMenu.Services.DepsRootHolder.Path = dataPathResolver.GetDependenciesDir();

builder.Services.AddSingleton<ControlMenu.Common.Paths.IDataPathResolver>(dataPathResolver);
builder.Configuration["DependenciesRoot"] = dataPathResolver.GetDependenciesDir();

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Icon Converter ships image bytes (base64) over the SignalR circuit;
        // default 32KB cap rejects anything but a tiny image.
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024; // 32 MB
    });

// Database — factory pattern required for Blazor Server (avoids stale change-tracker state).
// Connection string is built from the resolver, not from appsettings.json. This puts
// the SQLite file at <dataRoot>/config/controlmenu.db in production and at
// AppContext.BaseDirectory/controlmenu.db in dev.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dataPathResolver.GetDbPath()}"));

// Data Protection (used by SecretStore for encrypting settings).
// Keys directory is created above as part of resolver bootstrap, so the
// PersistKeysToFileSystem call here just needs to point at it.
// DPAPI on Windows encrypts the key file at rest with the machine key;
// without it Serilog logs "No XML encryptor configured" and the key XML
// sits in plaintext under <dataRoot>/keys/.
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataPathResolver.GetKeysDir()))
    .SetApplicationName("ControlMenu");
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}

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
builder.Services.AddSingleton<ControlMenu.Services.Update.IVelopackUpdateService, ControlMenu.Services.Update.VelopackUpdateService>();

// Android Devices module services
builder.Services.AddScoped<IAdbService, AdbService>();

// ws-scrcpy-web process management
builder.Services.AddSingleton<WsScrcpyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WsScrcpyService>());

// Network scanner — singleton holds the ws-scan WebSocket and fans events to subscribers
builder.Services.AddSingleton<INetworkScanService, NetworkScanService>();
builder.Services.AddScoped<IScanLifecycleHandler, ScanLifecycleHandler>();
builder.Services.AddScoped<IDeviceQuickScanService, DeviceQuickScanService>();
builder.Services.AddScoped<SubnetDetectionClient>();

// Jellyfin module services
builder.Services.AddScoped<IJellyfinDirectoryResolver, JellyfinDirectoryResolver>();
builder.Services.AddScoped<IJellyfinService, JellyfinService>();
builder.Services.AddScoped<IBackgroundJobService, BackgroundJobService>();

// Utilities module services
builder.Services.AddSingleton<IFileUnblockService, FileUnblockService>();

// Imaging Tools module services (magick-backed)
builder.Services.AddSingleton<ControlMenu.Modules.Imaging.Services.IImageService,
                              ControlMenu.Modules.Imaging.Services.ImageService>();
// Tracing service (raster -> SVG: vtracer color + potrace monochrome). Separate from
// IImageService because it drives the vtracer/potrace bundled binaries, not just magick.
builder.Services.AddSingleton<ControlMenu.Modules.Imaging.Services.ITracingService,
                              ControlMenu.Modules.Imaging.Services.TracingService>();

// Cameras module services
builder.Services.AddSingleton<ICameraChangeNotifier, CameraChangeNotifier>();
builder.Services.AddScoped<ICameraService, CameraService>();
builder.Services.AddSingleton<IOnvifDiscoveryClient, OnvifDiscoveryClient>();
builder.Services.AddScoped<IOnvifClient, OnvifClient>();
builder.Services.AddScoped<IHikvisionIsapiClient, HikvisionIsapiClient>();
builder.Services.AddSingleton<IRtspProbeClient, RtspProbeClient>();
builder.Services.AddSingleton<ICameraScanService, CameraScanService>();
builder.Services.AddSingleton<IntervalChangeSignal>();
builder.Services.AddHostedService<CameraLivenessHostedService>();
builder.Services.AddHostedService<AndroidLivenessHostedService>();
builder.Services.AddScoped<PurgeLegacyCameraSettingsMigration>();

// go2rtc streaming service
builder.Services.AddSingleton<IGo2RtcService, Go2RtcService>();
builder.Services.AddHostedService(sp => (Go2RtcService)sp.GetRequiredService<IGo2RtcService>());

// Dependency management
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("github-api", c =>
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ControlMenu"));
builder.Services.AddHttpClient("dependency-updates", c =>
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ControlMenu"));
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
builder.Services.AddHostedService<ObsoleteSettingsCleanupService>();

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

    var purge = scope.ServiceProvider.GetRequiredService<PurgeLegacyCameraSettingsMigration>();
    await purge.RunAsync();

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

    // Load enabled camera names for sidebar nav entries (module can't do async)
    var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();
    var allCameras = await cameraService.GetAllAsync();
    CamerasModule.EnabledCameras = allCameras
        .Where(c => c.Enabled)
        .Select(c => (c.Id, c.Name))
        .ToList();
}

// Refresh sidebar nav when cameras change (Add/Update/Delete/Enabled-toggle/Rename)
var cameraNotifier = app.Services.GetRequiredService<ICameraChangeNotifier>();
cameraNotifier.CamerasChanged += () =>
{
    using var scope = app.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<ICameraService>();
    var fresh = svc.GetAllAsync().GetAwaiter().GetResult();
    CamerasModule.EnabledCameras = fresh
        .Where(c => c.Enabled)
        .Select(c => (c.Id, c.Name))
        .ToList();
};

await app.RunAsync();
