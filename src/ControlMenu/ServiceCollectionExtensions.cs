using System.Reflection;
using ControlMenu.Common.Paths;
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
using ControlMenu.Services.Archive;
using ControlMenu.Services.Network;
using ControlMenu.Services.Startup;
using ControlMenu.Services.Verification;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers Control Menu's own application services on the DI container.
/// </summary>
/// <remarks>
/// Extracted from Program.cs so the complete service graph can be built and
/// validated in a unit test (ValidateScopes + ValidateOnBuild) -- see
/// DependencyInjectionValidationTests. The v1.2.0 imaging-services captive
/// dependency (Singleton service depending on the scoped IDependencyPathResolver)
/// shipped past 444 green tests + CI precisely because nothing ever built this
/// container: bUnit assembles its own minimal service collections, and the
/// packaged app runs in Production where scope validation is off. This extension
/// is the single seam that makes the real registrations testable.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Control Menu application service, hosted service, and
    /// supporting infrastructure (DbContext factory, Data Protection, HTTP
    /// clients, module discovery) on <paramref name="services"/>.
    /// </summary>
    /// <param name="dataPathResolver">
    /// The process-wide writable-path resolver. Registered as a singleton and
    /// used to root the SQLite database and the Data Protection key ring. Passed
    /// in (rather than constructed here) so tests can supply a temp-rooted
    /// resolver and the production bootstrap keeps owning resolver creation.
    /// </param>
    public static IServiceCollection AddControlMenuServices(
        this IServiceCollection services,
        IDataPathResolver dataPathResolver)
    {
        services.AddSingleton<IDataPathResolver>(dataPathResolver);

        // Database -- factory pattern required for Blazor Server (avoids stale change-tracker state).
        // Connection string is built from the resolver, not from appsettings.json. This puts
        // the SQLite file at <dataRoot>/config/controlmenu.db in production and at
        // AppContext.BaseDirectory/controlmenu.db in dev.
        // The busy-timeout interceptor makes a writer wait for a competing writer's lock
        // (instead of immediately throwing "database is locked") — required now that
        // DependencyManagerService.CheckAllAsync runs checks concurrently.
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dataPathResolver.GetDbPath()}")
                   .AddInterceptors(new SqliteBusyTimeoutInterceptor()));

        // Data Protection (used by SecretStore for encrypting settings).
        // Keys directory is created during resolver bootstrap, so the
        // PersistKeysToFileSystem call here just needs to point at it.
        // DPAPI on Windows encrypts the key file at rest with the machine key;
        // without it Serilog logs "No XML encryptor configured" and the key XML
        // sits in plaintext under <dataRoot>/keys/.
        var dataProtection = services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataPathResolver.GetKeysDir()))
            .SetApplicationName("ControlMenu");
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        // Core services
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddScoped<ISecretStore, SecretStore>();
        services.AddScoped<IConfigurationService, ConfigurationService>();
        services.AddScoped<IDependencyPathResolver>(sp =>
            new DependencyPathResolver(
                sp.GetRequiredService<ModuleDiscoveryService>().Modules,
                sp.GetRequiredService<IConfigurationService>()));
        services.AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IDeviceTypeCache, DeviceTypeCache>();
        services.AddScoped<IEmailService, EmailService>();
        // ARP table source: managed IP Helper (GetIpNetTable) on Windows, `arp -a` shell on Linux.
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IArpTableProvider, WindowsArpTableProvider>();
        else
            services.AddSingleton<IArpTableProvider, ShellArpTableProvider>();
        services.AddSingleton<INetworkDiscoveryService, NetworkDiscoveryService>();

        services.AddSingleton<IScrcpyProbeService, ScrcpyProbeService>();
        services.AddSingleton<ControlMenu.Services.Update.UpdateApplyState>();
        services.AddSingleton<ControlMenu.Services.Update.IVelopackUpdateService, ControlMenu.Services.Update.VelopackUpdateService>();

        // Android Devices module services
        services.AddScoped<IAdbService, AdbService>();

        // ws-scrcpy-web process management
        services.AddSingleton<WsScrcpyService>();
        services.AddHostedService(sp => sp.GetRequiredService<WsScrcpyService>());

        // Network scanner -- singleton holds the ws-scan WebSocket and fans events to subscribers
        services.AddSingleton<INetworkScanService, NetworkScanService>();
        services.AddScoped<IScanLifecycleHandler, ScanLifecycleHandler>();
        services.AddScoped<IDeviceQuickScanService, DeviceQuickScanService>();
        services.AddScoped<IDeviceRegistrationService, DeviceRegistrationService>();
        services.AddScoped<SubnetDetectionClient>();

        // Jellyfin module services
        services.AddScoped<IJellyfinDirectoryResolver, JellyfinDirectoryResolver>();
        services.AddScoped<IJellyfinService, JellyfinService>();
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();

        // Utilities module services
        services.AddSingleton<IFileUnblockService, FileUnblockService>();

        // Imaging Tools module services (magick-backed). Scoped, NOT singleton: both depend on the
        // scoped IDependencyPathResolver (-> scoped IConfigurationService) and are stateless per-call
        // CLI wrappers consumed by scoped Blazor pages. Registering them singleton was a captive-
        // dependency bug that aborts Dev startup via ValidateOnBuild -- DependencyInjectionValidationTests
        // now guards this.
        services.AddScoped<ControlMenu.Modules.Imaging.Services.IImageService,
                           ControlMenu.Modules.Imaging.Services.ImageService>();
        // Tracing service (raster -> SVG: vtracer color + potrace monochrome). Separate from
        // IImageService because it drives the vtracer/potrace bundled binaries, not just magick.
        services.AddScoped<ControlMenu.Modules.Imaging.Services.ITracingService,
                           ControlMenu.Modules.Imaging.Services.TracingService>();

        // Cameras module services
        services.AddSingleton<ICameraChangeNotifier, CameraChangeNotifier>();
        services.AddScoped<ICameraService, CameraService>();
        services.AddSingleton<IOnvifDiscoveryClient, OnvifDiscoveryClient>();
        services.AddScoped<IOnvifClient, OnvifClient>();
        services.AddScoped<IHikvisionIsapiClient, HikvisionIsapiClient>();
        // Pooled handler (shared, recycled) for Hikvision ISAPI probes — avoids a socket-per-probe
        // leak during camera scans. 5s timeout preserves the previous per-call bound.
        services.AddHttpClient("hikvision-isapi", c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<IRtspProbeClient, RtspProbeClient>();
        services.AddSingleton<ICameraScanService, CameraScanService>();
        services.AddSingleton<IntervalChangeSignal>();
        services.AddHostedService<CameraLivenessHostedService>();
        services.AddHostedService<AndroidLivenessHostedService>();
        services.AddScoped<PurgeLegacyCameraSettingsMigration>();

        // go2rtc streaming service
        services.AddSingleton<IGo2RtcService, Go2RtcService>();
        services.AddHostedService(sp => (Go2RtcService)sp.GetRequiredService<IGo2RtcService>());

        // Dependency management
        services.AddHttpClient();
        services.AddHttpClient("github-api", c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ControlMenu"));
        services.AddHttpClient("dependency-updates", c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ControlMenu"));
        services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
        services.AddSingleton<IAuthenticodeInspector, WindowsAuthenticodeInspector>();
        services.AddScoped<IArtifactVerifier>(sp => new ArtifactVerifier(
            sp.GetRequiredService<IAuthenticodeInspector>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("dependency-updates")));
        services.AddScoped<IDependencyManagerService>(sp =>
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
            var verifier = sp.GetRequiredService<IArtifactVerifier>();
            var extractor = sp.GetRequiredService<IArchiveExtractor>();
            return new DependencyManagerService(dbFactory, modules, executor, httpFactory, config, wsScrcpy, go2Rtc, resolver, logger, verifier, extractor);
        });
        // The clock behind scheduled background work. Registered so hosted services can take a
        // TimeProvider dependency and tests can drive their schedules with a fake clock instead of
        // waiting real seconds -- see DependencyCheckHostedServiceTests.
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<DependencyCheckHostedService>();
        services.AddHostedService<ObsoleteSettingsCleanupService>();

        // Module discovery -- scans the main assembly for IToolModule implementations
        services.AddSingleton(new ModuleDiscoveryService(
            [Assembly.GetExecutingAssembly()]));

        return services;
    }
}
