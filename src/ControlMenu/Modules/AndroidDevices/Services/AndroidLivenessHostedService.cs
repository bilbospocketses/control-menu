using System.Net.Sockets;
using ControlMenu.Services;

namespace ControlMenu.Modules.AndroidDevices.Services;

/// <summary>
/// Periodic Android device liveness check. Iterates registered devices and
/// TCP-probes each one's ADB port; updates <c>LastSeen</c> on success. Does
/// NOT populate the Discovered panel — that path is reserved for user-initiated
/// Quick Refresh / Scan Network. Interval is user-editable via the
/// <c>discovery-interval</c> config key (0 disables, otherwise 300–3600).
/// Hot-reload via <see cref="IntervalChangeSignal.AndroidLiveness"/>.
/// </summary>
public class AndroidLivenessHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OuterTick = TimeSpan.FromSeconds(30);
    private const string IntervalKey = "discovery-interval";
    private const int DefaultIntervalSeconds = 300;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntervalChangeSignal _signal;
    private readonly ILogger<AndroidLivenessHostedService> _logger;
    private DateTime _lastTick = DateTime.MinValue;

    public AndroidLivenessHostedService(
        IServiceScopeFactory scopeFactory,
        IntervalChangeSignal signal,
        ILogger<AndroidLivenessHostedService> logger)
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

            var signalTask = _signal.WaitAsync(IntervalChangeSignal.AndroidLiveness);
            var delayTask = Task.Delay(OuterTick, stoppingToken);
            try { await Task.WhenAny(signalTask, delayTask); }
            catch (OperationCanceledException) { break; }
            if (stoppingToken.IsCancellationRequested) break;

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
            var intervalStr = await config.GetSettingAsync(IntervalKey);
            var interval = int.TryParse(intervalStr, out var i) ? i : DefaultIntervalSeconds;
            if (interval <= 0) return;

            var elapsed = DateTime.UtcNow - _lastTick;
            if (elapsed < TimeSpan.FromSeconds(interval)) return;

            var deviceService = scope.ServiceProvider.GetRequiredService<IDeviceService>();
            var devices = await deviceService.GetAllDevicesAsync();
            _lastTick = DateTime.UtcNow;
            if (devices.Count == 0) return;

            var hits = 0;
            foreach (var dev in devices)
            {
                if (stoppingToken.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(dev.LastKnownIp)) continue;

                var ok = await ProbeAsync(dev.LastKnownIp, dev.AdbPort, ProbeTimeout, stoppingToken);
                if (ok)
                {
                    await deviceService.UpdateLastSeenAsync(dev.Id, dev.LastKnownIp);
                    hits++;
                }
            }

            _logger.LogInformation("Android liveness tick: {Hits}/{Total} responsive", hits, devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Android liveness tick failed");
        }
    }

    private static async Task<bool> ProbeAsync(string ip, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ip, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
