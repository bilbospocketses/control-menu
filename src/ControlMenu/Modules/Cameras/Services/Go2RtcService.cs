using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using ControlMenu.Services;

namespace ControlMenu.Modules.Cameras.Services;

public interface IGo2RtcService
{
    bool IsRunning { get; }
    string BaseUrl { get; }
    Task RegenerateConfigAsync();
    Task StopAsync();
    void Restart();
}

public class Go2RtcService : IHostedService, IDisposable, IGo2RtcService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Go2RtcService> _logger;
    private readonly string _contentRoot;
    private readonly object _lock = new();
    private Process? _process;
    private int _crashCount;
    private DateTime _lastCrash = DateTime.MinValue;
    private bool _serviceReady;
    private bool _disposed;

    public string BaseUrl => "http://localhost:1984";
    public bool IsRunning => _serviceReady && _process is { HasExited: false };

    public Go2RtcService(
        IServiceScopeFactory scopeFactory,
        ILogger<Go2RtcService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _contentRoot = configuration.GetValue<string>(WebHostDefaults.ContentRootKey)
            ?? AppContext.BaseDirectory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var exePath = FindExecutable();
        if (exePath is null)
        {
            _logger.LogWarning("go2rtc not found in PATH — camera streaming unavailable");
            return;
        }

        await GenerateConfigAsync();
        await KillOrphanOnPortAsync(cancellationToken);

        lock (_lock) { SpawnProcess(exePath); }

        await WaitForReadyAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposed = true;
        _serviceReady = false;
        lock (_lock) { KillProcess(); }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        return StopAsync(CancellationToken.None);
    }

    public void Restart()
    {
        _disposed = false;
        _crashCount = 0;
        _serviceReady = false;
        var exePath = FindExecutable();
        if (exePath is null)
        {
            _logger.LogWarning("go2rtc not found — cannot restart");
            return;
        }
        lock (_lock)
        {
            KillProcess();
            SpawnProcess(exePath);
        }
        _ = WaitForReadyAsync();
    }

    public async Task RegenerateConfigAsync()
    {
        await GenerateConfigAsync();

        if (_process is { HasExited: false })
        {
            _logger.LogInformation("Restarting go2rtc after config change");
            _crashCount = 0;
            _disposed = false;

            var exePath = FindExecutable();
            if (exePath is null) return;

            lock (_lock)
            {
                KillProcess();
                SpawnProcess(exePath);
            }

            await WaitForReadyAsync();
        }
    }

    private async Task GenerateConfigAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();

        var cameras = await cameraService.GetConfiguredCamerasAsync();
        var sb = new StringBuilder();
        sb.AppendLine("streams:");

        foreach (var camera in cameras)
        {
            var creds = await cameraService.GetCredentialsAsync(camera.Index);
            if (creds is null) continue;

            var (username, password) = creds.Value;
            sb.AppendLine($"  camera-{camera.Index}: rtsp://{username}:{password}@{camera.IpAddress}:{camera.Port}");
        }

        sb.AppendLine("api:");
        sb.AppendLine("  listen: \":1984\"");

        var configPath = Path.Combine(_contentRoot, "go2rtc.yaml");
        await File.WriteAllTextAsync(configPath, sb.ToString());
        _logger.LogInformation("Wrote go2rtc config with {Count} stream(s) to {Path}", cameras.Count, configPath);
    }

    private string? FindExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "go2rtc.exe" : "go2rtc";

        // Check local dependency install path first (dep-path-go2rtc setting or default)
        var localPath = GetLocalInstallPathAsync().GetAwaiter().GetResult();
        if (localPath is not null)
        {
            var localExe = Path.Combine(localPath, name);
            if (File.Exists(localExe))
                return localExe;
        }

        // Fall back to system PATH
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private async Task<string?> GetLocalInstallPathAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
        var customPath = await config.GetSettingAsync("dep-path-go2rtc");
        if (!string.IsNullOrWhiteSpace(customPath))
            return customPath;

        // Walk up from base directory to find dependencies folder (matches CamerasModule.FindDepsRoot)
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, "dependencies", "go2rtc");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null) break;
            dir = parent;
        }

        return Path.Combine(_contentRoot, "dependencies", "go2rtc");
    }

    private void SpawnProcess(string exePath)
    {
        // Must be called under _lock
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _contentRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _logger.LogDebug("go2rtc: {Line}", e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _logger.LogWarning("go2rtc stderr: {Line}", e.Data);
        };
        _process.Exited += OnProcessExited;
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _logger.LogInformation("go2rtc started (PID {Pid})", _process.Id);
    }

    private async Task WaitForReadyAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient();
        for (var i = 0; i < 30; i++)
        {
            if (ct.IsCancellationRequested || _disposed) return;
            try
            {
                var response = await http.GetAsync(BaseUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    _serviceReady = true;
                    _logger.LogInformation("go2rtc ready at {Url}", BaseUrl);
                    return;
                }
            }
            catch { /* not ready yet */ }
            await Task.Delay(500, ct);
        }

        _logger.LogWarning("go2rtc did not become ready within 15 seconds");
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _serviceReady = false;
        _logger.LogWarning("go2rtc process exited");

        if (_disposed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastCrash).TotalSeconds < 30)
        {
            _crashCount++;
        }
        else
        {
            _crashCount = 1;
        }
        _lastCrash = now;

        if (_crashCount <= 2)
        {
            _logger.LogInformation("Restarting go2rtc in 2 seconds (attempt {Count}/2)...", _crashCount);
            _ = Task.Delay(2000).ContinueWith(async _ =>
            {
                if (_disposed) return;
                var exePath = FindExecutable();
                if (exePath is null) return;
                lock (_lock) { SpawnProcess(exePath); }
                await WaitForReadyAsync();
            });
        }
        else
        {
            _logger.LogError("go2rtc crashed 3 times within 30s — giving up");
        }
    }

    private void KillProcess()
    {
        // Must be called under _lock
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _logger.LogInformation("go2rtc stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill go2rtc process");
            }
        }
        _process?.Dispose();
        _process = null;
    }

    private async Task KillOrphanOnPortAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", 1984, cancellationToken);
        }
        catch
        {
            return; // Port is free
        }

        _logger.LogWarning("Port 1984 already in use — killing orphan go2rtc process");

        var pid = await FindPidOnPortAsync(cancellationToken);
        if (pid is null)
        {
            _logger.LogWarning("Could not determine PID on port 1984 — go2rtc may fail to start");
            return;
        }

        try
        {
            var orphan = Process.GetProcessById(pid.Value);
            orphan.Kill(entireProcessTree: true);
            orphan.WaitForExit(3000);
            _logger.LogInformation("Killed orphan process PID {Pid} on port 1984", pid.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill orphan PID {Pid} on port 1984", pid.Value);
        }
    }

    private async Task<int?> FindPidOnPortAsync(CancellationToken cancellationToken)
    {
        string fileName, arguments;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileName = "netstat";
            arguments = "-ano";
        }
        else
        {
            fileName = "lsof";
            arguments = "-t -i :1984";
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return int.TryParse(output.Trim(), out var lsofPid) ? lsofPid : null;
            }

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.Contains(":1984") || !trimmed.Contains("LISTENING"))
                    continue;

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out var netstatPid))
                    return netstatPid;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find PID on port 1984");
        }

        return null;
    }

    public void Dispose()
    {
        _disposed = true;
        _serviceReady = false;
        lock (_lock) { KillProcess(); }
        GC.SuppressFinalize(this);
    }
}
