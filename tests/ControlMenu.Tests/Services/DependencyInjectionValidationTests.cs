using ControlMenu.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ControlMenu.Tests.Services;

/// <summary>
/// Builds Control Menu's real DI container with scope + build validation enabled.
/// This is the guard the v1.2.0 imaging captive-dependency bug evaded: Singleton
/// IImageService/ITracingService depended on the scoped IDependencyPathResolver,
/// which aborts Dev startup (exit 82) via ValidateOnBuild -- yet 444 green tests +
/// CI missed it because nothing ever built the Program.cs container (bUnit builds
/// its own minimal collections; the packaged app runs Production with scope
/// validation off). AddControlMenuServices is the seam that makes it testable.
/// </summary>
public class DependencyInjectionValidationTests
{
    [Fact]
    public void AddControlMenuServices_BuildsWithFullValidation_DoesNotThrow()
    {
        // Temp-rooted resolver: the SQLite + Data Protection registrations close over
        // its paths, but nothing is opened or written here -- ValidateOnBuild validates
        // constructor call sites, it does not instantiate the services.
        var tempRoot = Path.Combine(
            Path.GetTempPath(), "cm-di-validation-" + Guid.NewGuid().ToString("N"));

        var services = new ServiceCollection();
        services.AddLogging();
        // Provided by the WebApplication host in production; VelopackUpdateService and
        // Go2RtcService consume it, so the validated graph needs it registered.
        services.AddSingleton<IHostApplicationLifetime>(new FakeHostApplicationLifetime());

        services.AddControlMenuServices(new TestPathResolver(tempRoot));

        var exception = Record.Exception(() =>
        {
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
        });

        Assert.Null(exception);
    }

    [Fact]
    public void BuildServiceProvider_WithFullValidation_CatchesCaptiveDependency()
    {
        // Guard the guard: prove these validation options actually detect the captive-
        // dependency shape (a singleton consuming a scoped service) that the imaging bug
        // was. If a future edit weakens the options, the test above would silently pass
        // while guarding nothing -- this one fails instead.
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<SingletonConsumer>();

        var exception = Assert.Throws<AggregateException>(() =>
            services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            }));

        Assert.Contains("Cannot consume scoped service", exception.ToString());
    }

    private sealed class ScopedDependency { }

    // Singleton depending on a scoped service -- the captive-dependency shape.
    private sealed class SingletonConsumer
    {
        public SingletonConsumer(ScopedDependency dependency) { }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
