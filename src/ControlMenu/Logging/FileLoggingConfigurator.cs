using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace ControlMenu.Logging;

/// <summary>
/// Configures Serilog to write structured logs to a rolling file at the
/// requested path. Without this, ASP.NET Core's default logger only emits
/// to stdout, which the Velopack-installed app loses to its detached console
/// window — see the v1.1.0-beta.1 fresh-VM smoke where post-mortem diagnosis
/// was impossible because no controlmenu.log existed.
///
/// Stdout output is preserved (Serilog's console sink + the host's existing
/// stdout pipe). The file sink is added on top.
/// </summary>
public static class FileLoggingConfigurator
{
    private static int _processExitHooked;

    /// <summary>
    /// Number of times the ProcessExit flush handler has actually been registered. Wired once for
    /// the lifetime of the process regardless of how many times <see cref="AddFileSink"/> runs —
    /// exposed for tests to assert that idempotency.
    /// </summary>
    internal static int ProcessExitFlushRegistrations { get; private set; }

    public static ILoggingBuilder AddFileSink(ILoggingBuilder builder, string logFilePath)
    {
        // Flush and release any previously-configured global logger before replacing it. In
        // production AddFileSink runs exactly once; this only matters for re-initialization
        // (tests, or an accidental second call) where it prevents leaking the old file handle.
        Log.CloseAndFlush();

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Logger = serilog;

        // dispose:false — the DI logging provider must NOT own the process-global Log.Logger's
        // lifetime. Previously this was dispose:true, so disposing the host's logger factory (host
        // shutdown, or a second host built in the same process) tore down live logging for every
        // other holder of Log.Logger. The configurator owns the logger; we flush it on process exit.
        builder.ClearProviders();
        builder.AddProvider(new SerilogLoggerProvider(serilog, dispose: false));

        if (Interlocked.Exchange(ref _processExitHooked, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => Log.CloseAndFlush();
            ProcessExitFlushRegistrations++;
        }

        return builder;
    }

    /// <summary>Flushes any buffered log events to disk. Safe to call multiple times.</summary>
    public static void CloseAndFlush() => Log.CloseAndFlush();
}
