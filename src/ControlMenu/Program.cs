using ControlMenu.Data;
using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Cameras.Migrations;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using ControlMenu.Services.Network;
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

// Control Menu application services. Every CM-owned registration lives in a
// testable IServiceCollection extension so the full graph can be built and
// validated with ValidateScopes + ValidateOnBuild (see
// DependencyInjectionValidationTests). The v1.2.0 imaging captive-dependency bug
// — a Singleton service depending on the scoped IDependencyPathResolver — shipped
// past 444 green tests + CI precisely because nothing ever built this container.
// dataPathResolver roots the SQLite DB and the Data Protection key ring.
builder.Services.AddControlMenuServices(dataPathResolver);

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
    CamerasModule.EnabledCameras = CamerasModule.ProjectEnabledNav(await cameraService.GetAllAsync());
}

// Refresh sidebar nav when cameras change (Add/Update/Delete/Enabled-toggle/Rename). CamerasChanged
// fires on the camera-CRUD thread, so offload to a worker — never block it on async — and keep the
// refresh exception-safe so a failed reload can't fault the notifying caller (#21).
var cameraNotifier = app.Services.GetRequiredService<ICameraChangeNotifier>();
var cameraNavScopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
cameraNotifier.CamerasChanged += () =>
    _ = Task.Run(() => CamerasModule.RefreshEnabledNavAsync(cameraNavScopeFactory, app.Logger));

// Resolve this BEFORE RunAsync. UpdateApplyState is a singleton, so holding the reference across
// shutdown is fine — resolving it afterwards is not: the provider is already disposed by then and
// GetRequiredService threw "ObjectDisposedException: Cannot access a disposed object. Object name:
// 'IServiceProvider'" as the very last thing the process did.
var updateApplyState = app.Services.GetRequiredService<ControlMenu.Services.Update.UpdateApplyState>();

await app.RunAsync();

// Return the apply-update exit code explicitly — the launcher reads 75 to swap in a downloaded
// update — instead of relying on a clobberable Environment.ExitCode set deep inside a service.
return updateApplyState.ApplyRequested
    ? ControlMenu.Services.Update.VelopackUpdateService.ExitCodeApplyUpdate
    : 0;
