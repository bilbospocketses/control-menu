// src/ControlMenu/Services/WsScrcpyService.cs
using System.Diagnostics;
using ControlMenu.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class WsScrcpyService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WsScrcpyService> _logger;
    private Process? _process;
    private int _crashCount;
    private DateTime _lastCrash = DateTime.MinValue;

    public string BaseUrl { get; private set; } = "http://localhost:8000";
    public bool IsRunning => _process is { HasExited: false };

    public WsScrcpyService(IServiceScopeFactory scopeFactory, ILogger<WsScrcpyService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = await GetWsScrcpyPathAsync();
        if (string.IsNullOrEmpty(path))
        {
            _logger.LogWarning("ws-scrcpy-web path not configured — screen mirroring unavailable");
            return;
        }

        var indexJs = Path.Combine(path, "dist", "index.js");
        if (!File.Exists(indexJs))
        {
            _logger.LogWarning("ws-scrcpy-web not found at {Path} — screen mirroring unavailable", indexJs);
            return;
        }

        SpawnProcess(indexJs);

        // Wait for HTTP 200
        using var http = new HttpClient();
        for (var i = 0; i < 30; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                var response = await http.GetAsync(BaseUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("ws-scrcpy-web ready at {Url}", BaseUrl);
                    return;
                }
            }
            catch { /* not ready yet */ }
            await Task.Delay(500, cancellationToken);
        }

        _logger.LogWarning("ws-scrcpy-web did not become ready within 15 seconds");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        KillProcess();
        return Task.CompletedTask;
    }

    private void SpawnProcess(string indexJs)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = indexJs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = { ["PORT"] = "8000" }
            },
            EnableRaisingEvents = true
        };

        _process.Exited += OnProcessExited;
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _logger.LogInformation("ws-scrcpy-web started (PID {Pid})", _process.Id);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogWarning("ws-scrcpy-web process exited");

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

        if (_crashCount <= 1)
        {
            _logger.LogInformation("Restarting ws-scrcpy-web in 2 seconds...");
            Task.Delay(2000).ContinueWith(_ =>
            {
                var indexJs = _process?.StartInfo.Arguments;
                if (!string.IsNullOrEmpty(indexJs))
                {
                    SpawnProcess(indexJs);
                }
            });
        }
        else
        {
            _logger.LogError("ws-scrcpy-web crashed twice within 30s — giving up");
        }
    }

    public void Restart()
    {
        _crashCount = 0;
        var indexJs = _process?.StartInfo.Arguments;
        KillProcess();
        if (!string.IsNullOrEmpty(indexJs))
        {
            SpawnProcess(indexJs);
        }
    }

    private void KillProcess()
    {
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
        KillProcess();
        GC.SuppressFinalize(this);
    }
}
