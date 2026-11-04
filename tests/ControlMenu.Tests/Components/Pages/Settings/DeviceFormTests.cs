using Bunit;
using ControlMenu.Components.Pages.Settings;
using ControlMenu.Data.Entities;
using Microsoft.AspNetCore.Components;
using ControlMenu.Tests.TestHelpers;

namespace ControlMenu.Tests.Components.Pages.Settings;

public class DeviceFormTests : BunitContext
{
    [Fact]
    public void Save_DisablesButtonWhileInFlight_PreventingDoubleSubmit()
    {
        var tcs = new TaskCompletionSource();
        var invocations = 0;
        var model = new Device { Name = "Living Room TV", MacAddress = "aa-bb-cc-dd-ee-ff", ModuleId = "android-devices" };

        var cut = Render<DeviceForm>(ps => ps
            .Add(p => p.Model, model)
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, async () =>
            {
                invocations++;
                await tcs.Task; // hold the save in-flight
            })));

        var button = cut.Find("button.btn-primary");
        Assert.False(button.HasAttribute("disabled")); // valid model, not yet saving

        button.Click(); // begins the in-flight save
        // While the save runs, the button is disabled so the user cannot submit again.
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));

        tcs.SetResult(); // complete the save
        cut.WaitForStateOrHung(
            () => !cut.Find("button.btn-primary").HasAttribute("disabled"));
        Assert.Equal(1, invocations);
    }
}
