using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.Jellyfin;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinModuleTests
{
    private readonly JellyfinModule _module = new();

    [Fact]
    public void Id_IsJellyfin()
    {
        Assert.Equal("jellyfin", _module.Id);
    }

    [Fact]
    public void DisplayName_IsJellyfinMediaServer()
    {
        Assert.Equal("Jellyfin", _module.DisplayName);
    }

    [Fact]
    public void Icon_IsFilmIcon()
    {
        Assert.Equal("bi-film", _module.Icon);
    }

    [Fact]
    public void Dependencies_IncludesDockerAndSqlite()
    {
        var deps = _module.Dependencies.ToList();
        Assert.Contains(deps, d => d.Name == "docker");
        Assert.Contains(deps, d => d.Name == "sqlite3");
    }

    [Fact]
    public void ConfigRequirements_IsEmpty_SettingsMovedToSettingsPage()
    {
        // Jellyfin config is now managed in Settings > Jellyfin tab directly
        Assert.Empty(_module.ConfigRequirements);
    }

    [Fact]
    public void NavEntries_IncludesDbUpdateAndCastCrew()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/jellyfin/db-update");
        Assert.Contains(entries, e => e.Href == "/jellyfin/cast-crew");
    }

    [Fact]
    public void BackgroundJobs_IncludesCastCrewUpdate()
    {
        var jobs = _module.GetBackgroundJobs().ToList();
        Assert.Single(jobs);
        Assert.Equal("cast-crew-update", jobs[0].JobType);
        Assert.True(jobs[0].IsLongRunning);
    }

    [Fact]
    public void SmtpConfigRequirements_MovedToGeneralSettings()
    {
        // SMTP settings are now in Settings > General, not in module config
        Assert.Empty(_module.ConfigRequirements);
    }
}
