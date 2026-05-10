using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using ControlMenu.Common.Paths;
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
    private readonly ICameraChangeNotifier _cameraNotifier;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Go2RtcService> _logger;
    private readonly string _configDir;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _regenGate = new(1, 1);
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
        ICameraChangeNotifier cameraNotifier,
        IHostApplicationLifetime appLifetime,
        IDataPathResolver dataPathResolver)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // go2rtc.yaml is written to the data-root config dir (outside current\) so that
        // go2rtc's spawned process does not hold a working-directory handle inside current\
        // during a Velopack swap (Gotcha 9). The binary is invoked with -config <absolutePath>
        // so it does not need to discover the config file from its working directory.
        _configDir = dataPathResolver.GetConfigDir();
        _cameraNotifier = cameraNotifier;
        _cameraNotifier.CamerasChanged += OnCamerasChanged;

        // Belt-and-suspenders: IHostedService.StopAsync is the canonical stop
        // hook, but ApplicationStopping fires earlier in the host lifecycle and
        // ProcessExit catches non-graceful shutdowns (terminal close, debugger
        // stop, force-quit) that may skip IHostedService.StopAsync entirely.
        // All three call into the same idempotent KillProcess path.
        appLifetime.ApplicationStopping.Register(() =>
        {
            _disposed = true;
            _serviceReady = false;
            lock (_lock) { KillProcess(); }
        });
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _disposed = true;
            _serviceReady = false;
            lock (_lock) { KillProcess(); }
        };
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
        // Kill EVERY existing go2rtc.exe regardless of port binding. Port-1984
        // detection only catches the one that successfully bound; orphans that
        // never bound (because port was already in use by yet another orphan)
        // would otherwise survive forever. See Bug 1/2 in the v1.0.0 polish
        // round — runtime CRUD churn could leak hundreds of instances.
        KillAllOrphans();
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
        // Serialize all regeneration calls. Concurrent CamerasChanged events
        // (e.g., bulk camera scan adds 8 cameras in rapid succession) would
        // otherwise all pass the alive-check before any reached the lock,
        // then each would kill+spawn through the lock — leaking processes
        // any time a kill silently fails (handle is reset to null while the
        // OS-level process is still alive).
        await _regenGate.WaitAsync();
        try
        {
            // GenerateConfigAsync now returns false when the YAML is unchanged,
            // letting us skip the restart entirely for liveness-triggered
            // CamerasChanged events that don't actually change go2rtc's view
            // of the world.
            var changed = await GenerateConfigAsync();
            if (!changed) return;

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
        finally
        {
            _regenGate.Release();
        }
    }

    /// <summary>Builds the go2rtc YAML and writes it to disk only if it
    /// differs from what's already there. Returns true if the file was
    /// written (config genuinely changed), false if the new content matched
    /// what was on disk (no-op skip — caller can avoid the kill+spawn).</summary>
    private async Task<bool> GenerateConfigAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();

        var cameras = await cameraService.GetEnabledAsync();
        var sb = new StringBuilder();
        sb.AppendLine("streams:");

        foreach (var cam in cameras)
        {
            var creds = await cameraService.GetCredentialsAsync(cam.Id);
            if (creds is null)
            {
                _logger.LogWarning("Camera {Name} ({Id}) skipped — no credentials stored", cam.Name, cam.Id);
                continue;
            }
            var (username, password) = creds.Value;

            // Prefer the resolved RtspStreamUrl (full URL with auth replaced inline);
            // fall back to bare ip:port for ONVIF cameras whose discovery couldn't probe streams.
            string streamLine;
            if (!string.IsNullOrEmpty(cam.RtspStreamUrl))
            {
                var uri = new Uri(cam.RtspStreamUrl);
                streamLine = $"{uri.Scheme}://{username}:{password}@{uri.Host}:{uri.Port}{uri.PathAndQuery}";
            }
            else
            {
                streamLine = $"rtsp://{username}:{password}@{cam.IpAddress}:{cam.Port}";
            }

            sb.AppendLine($"  camera-{cam.Id:N}: {streamLine}");
        }

        sb.AppendLine("api:");
        sb.AppendLine("  listen: \":1984\"");

        var newContent = sb.ToString();
        // Config is written to _configDir (outside current\) — see SpawnProcess comment.
        var configPath = Path.Combine(_configDir, "go2rtc.yaml");

        // Skip the file write AND the downstream kill+spawn if the config is
        // unchanged. Liveness probes fire ICameraChangeNotifier.CamerasChanged
        // on every successful tick (so the Settings UI status dots refresh),
        // which triggers RegenerateConfigAsync. LastSeen isn't part of the
        // go2rtc YAML, so most of these regen calls produce identical output.
        // Pre-fix this caused N kill+spawn cycles per liveness interval; with
        // KillProcess silent failures sprinkled in, hundreds of zombies could
        // accumulate over a long session.
        if (File.Exists(configPath))
        {
            var existing = await File.ReadAllTextAsync(configPath);
            if (existing == newContent)
            {
                return false;
            }
        }

        await File.WriteAllTextAsync(configPath, newContent);
        _logger.LogInformation("Wrote go2rtc config with {Count} stream(s) to {Path}", cameras.Count, configPath);
        return true;
    }

    private void OnCamerasChanged() => _ = RegenerateConfigAsync();

    private string? FindExecutable()
    {
        using var scope = _scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDependencyPathResolver>();
        try
        {
            return resolver.ResolveAsync("cameras", "go2rtc").GetAwaiter().GetResult();
        }
        catch (DependencyNotInstalledException)
        {
            return null;
        }
    }

    private void SpawnProcess(string exePath)
    {
        // Must be called under _lock
        //
        // WorkingDirectory is anchored at the binary's own directory (Gotcha 9): go2rtc.exe
        // lives under <dataRoot>/dependencies/cameras/go2rtc/, which is outside current\.
        // The config file path is passed explicitly via -config so go2rtc does not need to
        // discover it from its working directory.
        var configPath = Path.Combine(_configDir, "go2rtc.yaml");
        var binaryDir = Path.GetDirectoryName(exePath)
            ?? throw new InvalidOperationException($"Cannot determine directory for go2rtc at: {exePath}");

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"-config \"{configPath}\"",
                WorkingDirectory = binaryDir,
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
            var pid = _process.Id;

            // CRITICAL: detach the Exited handler before killing. Without this,
            // the intentional kill triggers OnProcessExited which can't tell
            // "we killed it" from "it crashed" and schedules a phantom respawn
            // 2 seconds later. That phantom couldn't bind port 1984 (the real
            // replacement was already spawned), but stayed alive anyway —
            // leaking +1 process per CRUD operation.
            _process.Exited -= OnProcessExited;

            try
            {
                _process.Kill(entireProcessTree: true);
                // Process.Kill is async at the OS level. Wait for confirmation
                // before returning; without this, the next SpawnProcess can
                // race with the dying instance and bind-fail on port 1984.
                if (!_process.WaitForExit(3000))
                {
                    _logger.LogWarning("go2rtc PID {Pid} did not exit within 3s of Kill; sweeping orphans defensively", pid);
                    KillAllOrphans();
                }
                else
                {
                    _logger.LogInformation("go2rtc stopped (PID {Pid})", pid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill go2rtc PID {Pid}; sweeping orphans defensively", pid);
                KillAllOrphans();
            }
        }
        _process?.Dispose();
        _process = null;
    }

    /// <summary>Kills every go2rtc.exe (or go2rtc on Linux) on the machine
    /// except _process. Defensive sweep used at startup and as a fallback
    /// when KillProcess can't confirm a clean exit. The thinking: if our
    /// handle to the spawned process is uncertain or stale, the process is
    /// no longer "ours" and it's safer to terminate everything by name and
    /// let StartAsync respawn one fresh instance.</summary>
    private void KillAllOrphans()
    {
        var exeName = OperatingSystem.IsWindows() ? "go2rtc" : "go2rtc";
        var killed = 0;
        foreach (var proc in Process.GetProcessesByName(exeName))
        {
            try
            {
                if (_process is not null && proc.Id == _process.Id)
                {
                    proc.Dispose();
                    continue;
                }
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(2000);
                killed++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not kill go2rtc PID {Pid} during orphan sweep", proc.Id);
            }
            finally
            {
                proc.Dispose();
            }
        }
        if (killed > 0)
        {
            _logger.LogWarning("Orphan sweep killed {Count} stray go2rtc instance(s)", killed);
        }
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
            // netstat / lsof are OS-builtin utilities (CLAUDE.md PATH exception). They
            // operate on system tables and do not depend on working directory; no
            // WorkingDirectory override is needed here (Gotcha 9 does not apply to them).
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
        _cameraNotifier.CamerasChanged -= OnCamerasChanged;
        _disposed = true;
        _serviceReady = false;
        lock (_lock) { KillProcess(); }
        GC.SuppressFinalize(this);
    }
}
