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
    public static ILoggingBuilder AddFileSink(ILoggingBuilder builder, string logFilePath)
    {
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

        builder.ClearProviders();
        builder.AddProvider(new SerilogLoggerProvider(serilog, dispose: true));

        return builder;
    }

    /// <summary>Flushes any buffered log events to disk. Safe to call multiple times.</summary>
    public static void CloseAndFlush() => Log.CloseAndFlush();
}
