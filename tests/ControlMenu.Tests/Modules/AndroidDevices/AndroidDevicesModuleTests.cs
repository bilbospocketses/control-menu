using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.AndroidDevices;
using ControlMenu.Services;
using ControlMenu.Tests.Services.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ControlMenu.Tests.Modules.AndroidDevices;

public class AndroidDevicesModuleTests
{
    private readonly AndroidDevicesModule _module = new();

    [Fact]
    public void Id_IsAndroidDevices() => Assert.Equal("android-devices", _module.Id);

    [Fact]
    public void DisplayName_IsAndroidDevices() => Assert.Equal("Android Devices", _module.DisplayName);

    [Fact]
    public void Icon_IsPhoneIcon() => Assert.Equal("bi-phone", _module.Icon);

    [Fact]
    public void Dependencies_IncludesAdbAndScrcpy()
    {
        var deps = _module.Dependencies.ToList();
        Assert.Contains(deps, d => d.Name == "adb");
        Assert.Contains(deps, d => d.Name == "scrcpy");
    }

    [Fact]
    public void NavEntries_Includes5EntriesForDeviceListAndFourDeviceTypes()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Equal(5, entries.Count);
        Assert.Contains(entries, e => e.Href == "/android/devices");
        Assert.Contains(entries, e => e.Href == "/android/googletv");
        Assert.Contains(entries, e => e.Href == "/android/phone");
        Assert.Contains(entries, e => e.Href == "/android/tablet");
        Assert.Contains(entries, e => e.Href == "/android/watch");
    }

    [Fact]
    public void NavEntries_UseSvgIconPaths()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Equal("/images/devices/device-list.svg", entries.First(e => e.Href == "/android/devices").Icon);
        Assert.Equal("/images/devices/smart-tv.svg",    entries.First(e => e.Href == "/android/googletv").Icon);
        Assert.Equal("/images/devices/smart-phone.svg", entries.First(e => e.Href == "/android/phone").Icon);
        Assert.Equal("/images/devices/tablet.svg",      entries.First(e => e.Href == "/android/tablet").Icon);
        Assert.Equal("/images/devices/smart-watch.svg", entries.First(e => e.Href == "/android/watch").Icon);
    }

    [Fact]
    public void NavEntries_DeviceListIsAlwaysVisible()
    {
        var deviceList = _module.GetNavEntries().First(e => e.Href == "/android/devices");
        Assert.Null(deviceList.IsVisible);
    }

    [Fact]
    public void NavEntries_DeviceTypeEntriesHaveIsVisiblePredicate()
    {
        var entries = _module.GetNavEntries().ToList();
        foreach (var href in new[] { "/android/googletv", "/android/phone", "/android/tablet", "/android/watch" })
        {
            var entry = entries.First(e => e.Href == href);
            Assert.NotNull(entry.IsVisible);
        }
    }

    [Fact]
    public void NavEntries_PhoneEntry_IsVisible_TrueWhenCacheSaysTrue()
    {
        var phone = _module.GetNavEntries().First(e => e.Href == "/android/phone");
        var sp = BuildServiceProviderWithTypes(DeviceType.AndroidPhone);

        Assert.True(phone.IsVisible!(sp));
    }

    [Fact]
    public void NavEntries_PhoneEntry_IsVisible_FalseWhenCacheSaysFalse()
    {
        var phone = _module.GetNavEntries().First(e => e.Href == "/android/phone");
        var sp = BuildServiceProviderWithTypes(DeviceType.AndroidTablet);

        Assert.False(phone.IsVisible!(sp));
    }

    [Fact]
    public void GetBackgroundJobs_ReturnsEmpty() => Assert.Empty(_module.GetBackgroundJobs());

    [Fact]
    public void AdbDependency_HasCorrectVersionCommand()
    {
        var adb = _module.Dependencies.First(d => d.Name == "adb");
        Assert.Equal("adb --version", adb.VersionCommand);
        Assert.Equal("adb", adb.ExecutableName);
    }

    [Fact]
    public void ScrcpyDependency_HasGitHubSource()
    {
        var scrcpy = _module.Dependencies.First(d => d.Name == "scrcpy");
        Assert.Equal(UpdateSourceType.GitHub, scrcpy.SourceType);
        Assert.Equal("Genymobile/scrcpy", scrcpy.GitHubRepo);
    }

    private static IServiceProvider BuildServiceProviderWithTypes(params DeviceType[] typesPresent)
    {
        var services = new ServiceCollection();
        var fakeDeviceService = new FakeDeviceService();
        foreach (var t in typesPresent)
            fakeDeviceService.Devices.Add(new Device { Id = Guid.NewGuid(), Name = "D", Type = t, MacAddress = "aa", ModuleId = "android-devices" });

        services.AddSingleton<IDeviceService>(fakeDeviceService);
        services.AddSingleton<IDeviceChangeNotifier, DeviceChangeNotifier>();
        services.AddSingleton<IDeviceTypeCache>(sp =>
        {
            var cache = new DeviceTypeCache(
                sp.GetRequiredService<IDeviceService>(),
                sp.GetRequiredService<IDeviceChangeNotifier>());
            cache.RefreshAsync().GetAwaiter().GetResult();
            return cache;
        });
        return services.BuildServiceProvider();
    }
}
