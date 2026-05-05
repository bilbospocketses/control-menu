using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;

namespace ControlMenu.Modules.Cameras.Network;

/// <summary>
/// Periodic camera liveness check. Iterates registered enabled cameras and
/// TCP-probes each one's RTSP port directly; updates <c>LastSeen</c> on
/// success. Does NOT populate the Discovered panel — that path is reserved for
/// user-initiated <see cref="ICameraScanService.StartScanAsync"/> via the
/// settings UI. Interval is user-editable via the
/// <c>cameras-liveness-interval-seconds</c> config key (0 disables, otherwise
/// 300–3600). Hot-reload via <see cref="IntervalChangeSignal.CameraLiveness"/>.
/// </summary>
public class CameraLivenessHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OuterTick = TimeSpan.FromSeconds(30);
    private const string IntervalKey = "cameras-liveness-interval-seconds";
    private const string Module = "cameras";
    private const int DefaultIntervalSeconds = 900;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntervalChangeSignal _signal;
    private readonly ILogger<CameraLivenessHostedService> _logger;
    private DateTime _lastTick = DateTime.MinValue;

    public CameraLivenessHostedService(
        IServiceScopeFactory scopeFactory,
        IntervalChangeSignal signal,
        ILogger<CameraLivenessHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOneTickForTestsAsync(stoppingToken);

            var signalTask = _signal.WaitAsync(IntervalChangeSignal.CameraLiveness);
            var delayTask = Task.Delay(OuterTick, stoppingToken);
            try { await Task.WhenAny(signalTask, delayTask); }
            catch (OperationCanceledException) { break; }
            if (stoppingToken.IsCancellationRequested) break;

            // Signal fired → user changed interval; reset cadence so next tick re-evaluates immediately.
            if (signalTask.IsCompleted)
                _lastTick = DateTime.MinValue;
        }
    }

    /// <summary>Single iteration of the loop, exposed for tests.</summary>
    public async Task RunOneTickForTestsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var intervalStr = await config.GetSettingAsync(IntervalKey, Module);
            var interval = int.TryParse(intervalStr, out var i) ? i : DefaultIntervalSeconds;
            if (interval <= 0) return;

            var elapsed = DateTime.UtcNow - _lastTick;
            if (elapsed < TimeSpan.FromSeconds(interval)) return;

            var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();
            var probe = scope.ServiceProvider.GetRequiredService<IRtspProbeClient>();

            var cameras = await cameraService.GetEnabledAsync();
            _lastTick = DateTime.UtcNow;
            if (cameras.Count == 0) return;

            var hits = 0;
            foreach (var cam in cameras)
            {
                if (stoppingToken.IsCancellationRequested) break;
                var ok = await probe.ProbeTcpAsync(cam.IpAddress, cam.Port, ProbeTimeout, stoppingToken);
                if (ok)
                {
                    await cameraService.UpdateLastSeenAsync(cam.Id);
                    hits++;
                }
            }

            _logger.LogInformation("Camera liveness tick: {Hits}/{Total} responsive", hits, cameras.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Camera liveness tick failed");
        }
    }
}
