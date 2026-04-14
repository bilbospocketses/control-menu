# Phone USB Setup Wizard & Portrait Mirror Layout

**Date:** 2026-04-14

## Problem

Android phones require a USB-first procedure to enable wireless ADB (`adb tcpip {port}`), but there's no guided flow for this anywhere in the app. The "Reset ADB Port" button on the Pixel Dashboard runs the command but provides no context. Additionally, phones reboot regularly, which kills TCP mode — users need to re-run the procedure without re-adding the device.

Separately, the scrcpy mirror iframe is styled for landscape (TV) streams. Phones stream in portrait and need a taller, narrower container.

## Design

### 1. Shared `UsbSetupWizard` Component

A self-contained step-by-step component at `Components/Shared/UsbSetupWizard.razor`.

**Parameters:**
- `int Port` — the device's ADB port (default 5555)
- `string? Ip` — device IP if known (for the final connect step)
- `string? MacAddress` — for IP resolution if IP not known
- `EventCallback<bool> OnComplete` — fires with success/failure when wizard finishes or is dismissed

**Steps:**

1. **Connect USB** — Instructional text: "Connect your phone to this computer with a USB cable. Make sure USB Debugging is enabled in Developer Options." Button: "I've Connected" → runs `adb devices`, checks for a USB-attached device (serial without `:` port). If none found, shows warning and lets user retry.

2. **Enable Wireless ADB** — Runs `adb tcpip {port}` automatically. Shows spinner, then result. If success, advances. If failure, shows error with retry button.

3. **Connect Wirelessly** — Text: "Disconnect the USB cable now." Button: "Done, Connect Wirelessly" → runs `adb connect {ip}:{port}`. If IP is null, attempts MAC-based resolution first. Shows success/failure. On success, fires `OnComplete(true)`.

Each step shows a step indicator (1/3, 2/3, 3/3). A "Cancel" link is always available.

### 2. Integration Points

**DeviceForm (post-add):** When a new `AndroidPhone` device is saved, the parent (`DeviceManagement` or `WizardDevices`) shows the `UsbSetupWizard` in a dialog overlay. The wizard receives the just-saved device's port, IP, and MAC.

**Pixel Dashboard:** Replace the "Reset ADB Port" action row with an "Enable Wireless ADB" button that expands the `UsbSetupWizard` inline below the quick actions panel. The existing `ResetAdbPort` method is removed since the wizard subsumes it.

### 3. Portrait Mirror Layout

**Pixel Dashboard CSS:** Change the grid layout from side-by-side (controls left, mirror right) to stacked (controls top, mirror below) with the mirror constrained to a portrait aspect ratio.

```css
.dashboard-layout {
    display: flex;
    flex-direction: column;
    gap: 1.25rem;
}
.mirror-panel {
    max-width: 400px;
    aspect-ratio: 9 / 16;
}
```

The controls panel becomes full-width on top, and the portrait mirror sits below it. This avoids the awkward landscape-shaped container that squishes a portrait video stream.

**GoogleTV Dashboard:** No changes. Keeps the existing `grid-template-columns: 280px 1fr` landscape layout.

### 4. ADB Service Changes

Add one method to `IAdbService` / `AdbService`:

```csharp
Task<IReadOnlyList<string>> GetUsbDevicesAsync(CancellationToken ct = default);
```

Runs `adb devices` and returns serials that don't contain `:` (USB-attached, not network-connected).

## Files Changed

- **New:** `Components/Shared/UsbSetupWizard.razor` + `.razor.css`
- **Modified:** `Modules/AndroidDevices/Services/AdbService.cs` — add `GetUsbDevicesAsync`
- **Modified:** `Modules/AndroidDevices/Services/IAdbService.cs` — add interface method
- **Modified:** `Modules/AndroidDevices/Pages/PixelDashboard.razor` — replace Reset ADB Port with wizard, update layout
- **Modified:** `Modules/AndroidDevices/Pages/PixelDashboard.razor.css` — portrait layout
- **Modified:** `Components/Pages/Settings/DeviceManagement.razor` — show wizard after phone add
- **Modified:** `Components/Pages/Setup/WizardDevices.razor` — show wizard after phone add (if applicable)
