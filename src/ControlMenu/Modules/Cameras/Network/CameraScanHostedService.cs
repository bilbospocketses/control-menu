using System.Text.Json;
using ControlMenu.Services;
using ControlMenu.Services.Network;

namespace ControlMenu.Modules.Cameras.Network;

/// <summary>
/// Abstraction over <see cref="SubnetDetectionClient"/> to allow test mocking
/// (SubnetDetectionClient is sealed with a non-virtual DetectAsync).
/// </summary>
public interface ICameraSubnetDetector
{
    Task<DetectedSubnet?> DetectAsync(CancellationToken ct);
}

/// <summary>
/// Production wrapper that delegates to <see cref="SubnetDetectionClient"/>.
/// DI registration deferred to T18.
/// </summary>
public class CameraSubnetDetector : ICameraSubnetDetector
{
    private readonly SubnetDetectionClient _inner;

    public CameraSubnetDetector(SubnetDetectionClient inner) => _inner = inner;

    public Task<DetectedSubnet?> DetectAsync(CancellationToken ct) => _inner.DetectAsync(ct);
}

public class CameraScanHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private const string IntervalKey = "cameras-scan-interval-minutes";
    private const string SubnetsKey = "cameras-scan-subnets";
    private const string Module = "cameras";
    private const int DefaultIntervalMinutes = 15;

    private readonly ICameraScanService _scan;
    private readonly IConfigurationService _config;
    private readonly ICameraSubnetDetector _detect;
    private readonly ILogger<CameraScanHostedService> _logger;
    private DateTime _lastTick = DateTime.MinValue;

    public CameraScanHostedService(
        ICameraScanService scan,
        IConfigurationService config,
        ICameraSubnetDetector detect,
        ILogger<CameraScanHostedService> logger)
    {
        _scan = scan; _config = config; _detect = detect; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOneTickForTestsAsync(stoppingToken);
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Single iteration of the loop, exposed for tests.</summary>
    public async Task RunOneTickForTestsAsync(CancellationToken stoppingToken)
    {
        try
        {
            var intervalStr = await _config.GetSettingAsync(IntervalKey, Module);
            var interval = int.TryParse(intervalStr, out var i) ? i : DefaultIntervalMinutes;
            if (interval <= 0) return;
            if (_scan.Phase == ScanPhase.Scanning) return;

            var elapsed = DateTime.UtcNow - _lastTick;
            if (elapsed < TimeSpan.FromMinutes(interval)) return;

            var subnets = await ResolveSubnetsAsync(stoppingToken);
            if (subnets.Count == 0)
            {
                _logger.LogInformation("Camera scan: no subnets resolved, skipping tick");
                return;
            }

            _logger.LogInformation("Periodic camera scan starting against {Count} subnet(s)", subnets.Count);
            await _scan.StartScanAsync(subnets, CancellationToken.None);
            _lastTick = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Periodic camera scan tick failed");
        }
    }

    private async Task<IReadOnlyList<ParsedSubnet>> ResolveSubnetsAsync(CancellationToken ct)
    {
        var list = new List<ParsedSubnet>();
        var auto = await _detect.DetectAsync(ct);
        if (auto is not null)
        {
            var result = SubnetParser.Parse(auto.Cidr);
            if (result.IsSuccess && result.Value is not null)
                list.Add(result.Value);
        }

        var userJson = await _config.GetSettingAsync(SubnetsKey, Module);
        if (!string.IsNullOrEmpty(userJson))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<string[]>(userJson) ?? [];
                foreach (var raw in entries)
                {
                    var result = SubnetParser.Parse(raw);
                    if (result.IsSuccess && result.Value is not null)
                        list.Add(result.Value);
                }
            }
            catch (JsonException) { /* ignore malformed */ }
        }

        // Dedupe by Normalized
        return list.GroupBy(s => s.Normalized).Select(g => g.First()).ToList();
    }
}
