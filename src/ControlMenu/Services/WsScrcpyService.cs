// src/ControlMenu/Services/WsScrcpyService.cs
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ControlMenu.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public enum WsScrcpyDeployMode { Managed, External }

public class WsScrcpyService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfigurationService _config;
    private readonly ILogger<WsScrcpyService> _logger;
    private readonly object _lock = new();
    private Process? _process;
    private string? _indexJs;
    private int _crashCount;
    private DateTime _lastCrash = DateTime.MinValue;
    private bool _serviceReady;
    private bool _disposed;

    public string BaseUrl { get; private set; } = "http://localhost:8000";
    public bool IsRunning => _serviceReady && (_process is null || !_process.HasExited);

    public WsScrcpyService(IServiceScopeFactory scopeFactory, IConfigurationService config, ILogger<WsScrcpyService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<WsScrcpyDeployMode> GetDeployModeAsync(CancellationToken ct = default)
    {
        var raw = await _config.GetSettingAsync("wsscrcpy-mode");
        return string.Equals(raw, "external", StringComparison.OrdinalIgnoreCase)
            ? WsScrcpyDeployMode.External
            : WsScrcpyDeployMode.Managed;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var mode = await GetDeployModeAsync(cancellationToken);
        if (mode == WsScrcpyDeployMode.External)
        {
            var url = (await _config.GetSettingAsync("wsscrcpy-url")) ?? "http://localhost:8000";
            BaseUrl = url;
            _serviceReady = true;
            _logger.LogInformation("ws-scrcpy-web external mode, using URL {Url}", url);
            return;
        }

        var path = await GetWsScrcpyPathAsync();
        if (string.IsNullOrEmpty(path))
        {
            _logger.LogWarning("ws-scrcpy-web path not configured — screen mirroring unavailable");
            return;
        }

        _indexJs = Path.Combine(path, "dist", "index.js");
        if (!File.Exists(_indexJs))
        {
            _logger.LogWarning("ws-scrcpy-web not found at {Path} — screen mirroring unavailable", _indexJs);
            _indexJs = null;
            return;
        }

        await KillOrphanOnPortAsync(cancellationToken);

        lock (_lock) { SpawnProcess(); }

        await WaitForReadyAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposed = true;
        _serviceReady = false;
        lock (_lock) { KillProcess(); }
        return Task.CompletedTask;
    }

    private void SpawnProcess()
    {
        // Must be called under _lock
        if (_indexJs is null) return;
        var workingDir = Path.GetDirectoryName(Path.GetDirectoryName(_indexJs))!;
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = _indexJs,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = { ["PORT"] = "8000" }
            },
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _logger.LogDebug("ws-scrcpy-web: {Line}", e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _logger.LogWarning("ws-scrcpy-web stderr: {Line}", e.Data);
        };
        _process.Exited += OnProcessExited;
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _logger.LogInformation("ws-scrcpy-web started (PID {Pid})", _process.Id);
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
                    _logger.LogInformation("ws-scrcpy-web ready at {Url}", BaseUrl);
                    return;
                }
            }
            catch { /* not ready yet */ }
            await Task.Delay(500, ct);
        }

        _logger.LogWarning("ws-scrcpy-web did not become ready within 15 seconds");
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _serviceReady = false;
        _logger.LogWarning("ws-scrcpy-web process exited");

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
            _logger.LogInformation("Restarting ws-scrcpy-web in 2 seconds (attempt {Count}/2)...", _crashCount);
            _ = Task.Delay(2000).ContinueWith(async _ =>
            {
                if (_disposed || _indexJs is null) return;
                lock (_lock) { SpawnProcess(); }
                await WaitForReadyAsync();
            });
        }
        else
        {
            _logger.LogError("ws-scrcpy-web crashed 3 times within 30s — giving up");
        }
    }

    public void Restart()
    {
        // Managed mode only — External mode has nothing to restart.
        if (_config.GetSettingAsync("wsscrcpy-mode").Result?.ToLowerInvariant() == "external") return;
        _disposed = false;
        _crashCount = 0;
        _serviceReady = false;
        lock (_lock)
        {
            KillProcess();
            SpawnProcess();
        }
        _ = WaitForReadyAsync();
    }

    private void KillProcess()
    {
        // Must be called under _lock
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _logger.LogInformation("ws-scrcpy-web stopped");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill ws-scrcpy-web process");
            }
        }
        _process?.Dispose();
        _process = null;
    }

    private async Task KillOrphanOnPortAsync(CancellationToken cancellationToken)
    {
        // Quick check: is anything listening on our port?
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", 8000, cancellationToken);
        }
        catch
        {
            return; // Port is free
        }

        _logger.LogWarning("Port 8000 already in use — killing orphan ws-scrcpy-web process");

        var pid = await FindPidOnPortAsync(cancellationToken);
        if (pid is null)
        {
            _logger.LogWarning("Could not determine PID on port 8000 — ws-scrcpy-web may fail to start");
            return;
        }

        try
        {
            var orphan = Process.GetProcessById(pid.Value);
            orphan.Kill(entireProcessTree: true);
            orphan.WaitForExit(3000);
            _logger.LogInformation("Killed orphan process PID {Pid} on port 8000", pid.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill orphan PID {Pid} on port 8000", pid.Value);
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
            arguments = "-t -i :8000";
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
                // lsof -t returns just the PID
                return int.TryParse(output.Trim(), out var lsofPid) ? lsofPid : null;
            }

            // Parse netstat -ano: find LISTENING line with :8000
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.Contains(":8000") || !trimmed.Contains("LISTENING"))
                    continue;

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out var netstatPid))
                    return netstatPid;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find PID on port 8000");
        }

        return null;
    }

    private async Task<string?> GetWsScrcpyPathAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var setting = await db.Settings.FirstOrDefaultAsync(s =>
            s.Key == "ws_scrcpy_web_path" && s.ModuleId == "android-devices");
        return setting?.Value;
    }

    public void Dispose()
    {
        _disposed = true;
        _serviceReady = false;
        lock (_lock) { KillProcess(); }
        GC.SuppressFinalize(this);
    }
}
