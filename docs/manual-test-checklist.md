# Control Menu — Manual Test Checklist

Post-audit verification. Run the app with `dotnet run` from `src/ControlMenu/`.

---

## 1. Startup & Home Page

- [ ] App starts without errors in console
- [ ] Home page loads with hero section (app icon, title, subtitle)
- [ ] Module cards display: Android Devices, Cameras, Jellyfin, Utilities
- [ ] Settings card displays with links to: General, Devices, Jellyfin, Dependencies, Cameras
- [ ] Each module card shows brand logo (Android robot SVG, Jellyfin logo SVG, etc.)
- [ ] Pill-button navigation inside each card works (links to correct pages)
- [ ] "No modules loaded" message does NOT show (verifies module discovery works)

## 2. Theme & Layout

- [ ] Dark mode is the default
- [ ] Toggle to light mode — all text is readable, no white-on-white
- [ ] Toggle back to dark — no elements stuck in light colors
- [ ] Theme persists after page refresh (stored in localStorage)
- [ ] Sidebar pill buttons are styled, not plain blue links
- [ ] Emoji icons show in sidebar (not Bootstrap Icons)
- [ ] **Page title in TopBar updates** when navigating to different pages (not stuck on "Home")

## 3. Sidebar Persistence & Navigation

- [ ] First visit: all module groups expanded by default
- [ ] Collapse a module group, refresh page — group stays collapsed
- [ ] Expand/Collapse All button visible when sidebar is expanded
- [ ] Click "Collapse all" — all groups collapse, button changes to "Expand all"
- [ ] Click "Expand all" — all groups expand
- [ ] Expand/collapse state persists across page refresh (check `sidebar-expanded-groups` in localStorage)
- [ ] Collapsed sidebar (pill mode): expand/collapse all button is hidden
- [ ] App icon visible in sidebar header when expanded
- [ ] "Control Menu" title in sidebar is a clickable link to home

## 4. Settings > General

- [ ] Theme toggle works from settings
- [ ] SMTP server field: type a value, tab out — "Saved." appears, **value stays** (does not revert)
- [ ] SMTP port field: same test — value persists after save
- [ ] SMTP username: same test
- [ ] SMTP password: type a value, tab out — "Saved." appears
- [ ] SMTP password: clear the field, tab out — "Password cleared." appears
- [ ] Notification email: type and save, value persists
- [ ] "Send Test Email" button — shows result (success or failure message)
- [ ] "Re-run Setup Wizard" button — navigates to wizard

## 5. Settings > Devices

### 5a. Registered Devices table

- [ ] Existing devices appear in the table with Status dot, Name, Type, MAC, IP, ADB Port, Last Seen
- [ ] Status dot is green for devices seen in the last 10 minutes, yellow 10m–24h, grey >24h or never
- [ ] Click "Add Device" — form appears
- [ ] Fill form (Name / Type / ADB Port / MAC), click Save — device appears in table
- [ ] Device Type dropdown offers four options: GoogleTV, AndroidPhone, AndroidTablet, AndroidWatch
- [ ] Saving with `AndroidTablet` or `AndroidWatch` persists and shows the enum value in the Type column after reload
- [ ] Click "Edit" on a device — form populates with the current device data (including PIN for phones)
- [ ] **Edit a field, click Cancel** — original value is unchanged in the table (not mutated)
- [ ] Edit a field, click Save — change is persisted
- [ ] Delete a device — confirmation dialog appears, accepting removes the device from the table
- [ ] After deleting, the device's user-assigned name is stashed at `device-name-<mac>` in the Settings table (re-appears when re-added from Scan — verify in 5c below)
- [ ] For `AndroidPhone` type: "Screen Lock PIN" field appears in the form (password input, stored encrypted via Data Protection)

### 5b. Scan Network — existing devices

- [ ] Click "Scan Network" — button disables and changes to "Scanning..." while the scan runs
- [ ] Toast message on success reports `N of M registered device(s) found` and (when applicable) `K ADB port(s) updated from mDNS` + `X new device(s) on network`
- [ ] Registered devices' Last Seen timestamp refreshes to "Just now" for anything reachable
- [ ] If a registered device's ADB port has drifted since it was added (common on Android 11+ phones that rotate wireless-debugging ports), the scan updates the ADB Port column silently — no prompt needed
- [ ] Devices not advertising via mDNS are still IP-refreshed via ARP fallback (no port update for these, since ARP doesn't know the ADB port)

### 5c. Discovered on Network panel

- [ ] After a successful Scan Network, a "Discovered on Network" section appears **only if** mDNS found devices not already in the Registered Devices table
- [ ] Registered devices do NOT appear in this panel (they're silently refreshed in 5b instead)
- [ ] Each discovered row shows: Service label (e.g. `adb-49241HFAG07SUG`), IP, ADB Port, MAC, and an "Add" button
- [ ] MAC column shows `—` when the host's ARP table hasn't resolved the IP yet; the Add button is disabled for those rows
- [ ] Click Add on an unregistered discovered device:
  - [ ] Device form opens with MAC, ADB Port, and IP pre-filled from the scan
  - [ ] Name field is blank (fresh discovery) or populated with the previously-assigned name (if this MAC was deleted before — pulled from the `device-name-<mac>` setting)
  - [ ] Within ~1–2 seconds, the Type dropdown auto-refines from `AndroidPhone` default to the correct kind based on an ADB probe (watch → AndroidWatch, TV → GoogleTV, tablet → AndroidTablet, otherwise AndroidPhone)
  - [ ] Within the same probe, Name auto-fills from `ro.product.model` when it was blank (e.g., "Pixel 9", "Galaxy Tab A"). If Name was already remembered from a previous delete, the probe does NOT overwrite it.
- [ ] Save the pre-filled device — it moves up into the Registered Devices table AND disappears from the Discovered panel (no double-offering)
- [ ] Re-run Scan Network — the now-registered device is absent from Discovered (still registered, IP/port refreshed silently)

### 5d. Delete → rediscover flow

- [ ] Register a phone, give it a memorable name (e.g., "My Phone"), save
- [ ] Delete it
- [ ] Click Scan Network — the phone appears in the Discovered panel
- [ ] Click Add — the form opens with Name already populated with "My Phone" (restored from Settings)
- [ ] Save — device is re-added with its original name intact

### 5e. A1+A4 dynamic nav and dashboards

**Empty-inventory start:**
- [ ] Fresh install or all devices deleted. Sidebar shows only "Device List" under Android Devices.
- [ ] Navigate to `/android/phone` directly in the URL bar → redirected to `/android/devices`.

**First-device emergence:**
- [ ] On Device List page, scan and add an Android Phone. Sidebar updates within one render cycle to show "Android Phone" entry.
- [ ] Click the Android Phone entry → `/android/phone` loads the newly added device.

**Multi-type sidebar:**
- [ ] Add an Android Tablet. Both "Android Phone" and "Android Tablet" entries present in sidebar.
- [ ] Add a Google TV device. "Google TV" entry present too.

**Last-device deletion while on its dashboard:**
- [ ] Navigate to `/android/tablet`. Delete the only tablet from another browser tab or via the scanner. Current tab auto-redirects to `/android/devices`.
- [ ] Repeat for phone (`/android/phone`) and Google TV (`/android/googletv`).

**Watch dashboard cold load (no hardware test possible):**
- [ ] Register a watch device (manually via DB or via scanner if the device advertises as a watch). Navigate to `/android/watch`. Page renders with "Unlock Watch" label. `ScrcpyMirror` iframe is wired.

**Wizard flow (from fresh install):**
- [ ] Walk the setup wizard. On Devices step, `DiscoveredPanel` renders, Scan Network button works, discovered list populates.
- [ ] Use inline add on a discovered row → device appears in the "Registered devices" table below.
- [ ] Complete wizard → `/` → sidebar shows the added device types.
- [ ] Visual: verify the DiscoveredPanel table doesn't horizontally overflow the wizard content area (flagged in T14 review — `data-table-fixed` column widths total ~1105px).

**Collapsed sidebar:**
- [ ] Toggle sidebar collapsed. Device type entries still render as SVG icons (no text).
- [ ] Click the Android Phone SVG icon → navigates correctly to `/android/phone`.

**Icon visual check in both themes:**
- [ ] Verify custom SVG icons render correctly in light mode.
- [ ] Verify custom SVG icons render correctly in dark mode.
- [ ] Flag any contrast issues with the device-list icon (lighter content against dark sidebar).

## 6. Settings > Cameras

### 6a. Configured cameras table

- [ ] Page loads at `/settings/cameras` with Configured cameras table (empty on first-run after legacy purge)
- [ ] Click "Add Camera" — camera form appears
- [ ] Fill form (Name, IP, Port, Username, Password), click Save — camera appears in table
- [ ] Reload page — saved camera appears in table
- [ ] Configured camera shows in sidebar with custom name (e.g., "Front Door")
- [ ] Click "✎" (Edit) on a camera — edit modal opens with current data pre-filled
- [ ] Edit a field, click Cancel — original value unchanged in table
- [ ] Edit a field, click Save — change persists in table
- [ ] Toggle the Enabled checkbox off on a camera — camera disappears from live-view (within seconds)
- [ ] Toggle back on — camera reappears in live-view
- [ ] Delete a camera — confirmation dialog appears, accepting removes it from table
- [ ] After deletion, verify no orphan secret rows in database

#### Camera scanner

- [ ] Open Settings → Cameras. Configured-cameras table shows current cameras (or empty if first-run after legacy purge).
- [ ] Click "Scan Network". Subnet modal opens; auto-detected subnet is pre-filled.
- [ ] Run scan against `192.168.1.0/24` (or whatever your LAN is). Verify Hikvision/LTS cameras appear in the Discovered panel within ~5 seconds with manufacturer + model populated.
- [ ] In a discovered ONVIF row, type a known-good username + password and click Add. Camera moves to the Configured table. Open Cameras live-view: stream plays.
- [ ] Repeat with a wrong password. Inline error "Authentication failed at HH:MM:SS" appears, row stays in panel.
- [ ] Manually configure a non-ONVIF RTSP camera (or temporarily disable ONVIF on a test camera). Discovered panel shows it as "RTSP only". Click "Add manually...". Modal appears with IP pre-filled. Enter name, manufacturer, creds, stream path. Submit; camera saves and streams.
- [ ] In the Configured table, click ✎ on a camera. Edit modal opens. Rename + save; verify renamed in the table and (if applicable) in the live-view label.
- [ ] Toggle the Enabled checkbox off on a camera. Verify it disappears from the live view (within seconds — go2rtc restarts via notifier-driven regen).
- [ ] Delete a camera. Verify it's gone from the table; no orphan secret rows in the DB.
- [ ] Set `cameras-scan-interval-minutes` to `1`; wait ~90 seconds; verify periodic-scan log line appears. Set to `0`; verify scans stop within 1 minute.
- [ ] Add a subnet via the AddSubnetModal; verify next scan covers it.
- [ ] Cancel a long-running scan via the modal's Cancel button. Verify partial Hits remain visible; Phase returns to Idle.

## 7. Settings > Jellyfin

- [ ] Compose file path + Parse button works (if docker-compose.yml exists)
- [ ] Container name and DB path auto-populate from compose parse
- [ ] API key field: set value, tab out — saved
- [ ] API key field: clear value, tab out — "API key cleared." message
- [ ] Base URL, User ID, Cast/Crew email — save and persist correctly
- [ ] Backup retention setting saves
- [ ] Managed directories section shows stats (if dirs exist)

## 8. Settings > Dependencies

- [ ] Dependencies table loads with status badges
- [ ] **Status badges are styled** (colored pills, not plain text)
- [ ] "Check All" button runs checks, badges update
- [ ] If an update is available: "Update" button appears
- [ ] **Update dialog** has proper overlay + centered layout (not unstyled)
- [ ] **Disabled buttons** appear visually dimmed (not identical to enabled)

## 9. Setup Wizard (re-run from Settings > General)

- [ ] Step 1 (Welcome) loads
- [ ] Step 2 (Devices): Add a device, advance to Step 3, **click Back** — device still visible in table
- [ ] Step 3 (Cameras): camera slots appear, fill details for Camera 1
- [ ] Step 3: leave others empty, advance — only filled cameras are saved
- [ ] Step 3: click Back to Step 2, then forward — Camera 1 data persists
- [ ] Step 4 (Jellyfin): settings show as configured
- [ ] Step 5 (Email): SMTP fields and notification email
- [ ] Step 6 (Dependencies): scan runs, found items show green "Found" badges
- [ ] Step 6: For any "Not Found" item — click "Enter Path...", enter a valid path, click OK — validates and shows version
- [ ] Step 7 (Done) — shows summary, "Finish" completes wizard

## 10. Android > Google TV Dashboard

- [ ] Page loads without errors
- [ ] If device connected: controls appear, power status dot is colored
- [ ] Screensaver shows actual state (not always "Google" — may show "Unknown" if disconnected)
- [ ] Status messages appear and auto-dismiss after 5 seconds
- [ ] Screen mirror iframe loads (if ws-scrcpy-web is configured)
- [ ] ws-scrcpy-web toolbar inside the iframe defaults to **D-pad mode** (TV DeviceKind hint is being passed correctly)
- [ ] **Mouse clicks in mirror control the TV** (left-click = tap, right-click = back, middle = home)
- [ ] **Clicks continue working after quality protection stream refresh** (no dead clicks)
- [ ] Navigate away and back — no console errors about disposed components

## 11. Android > Android Phone Dashboard

- [ ] Page loads
- [ ] "Reset ADB Port" uses the device's configured port (check the status message)
- [ ] Connect/disconnect works
- [ ] Screen mirror loads in portrait orientation
- [ ] Mirror panel sizes dynamically from actual device screen dimensions (no black bars)
- [ ] ws-scrcpy-web toolbar inside the iframe defaults to **Touch mode** (phone DeviceKind hint is being passed correctly)
- [ ] **Phone Unlock:** If PIN configured, "Unlock" button sends PIN via ADB
- [ ] **Phone Unlock:** If no PIN, shows "Set PIN in Settings" link

> USB Setup Wizard was removed in favor of mDNS device discovery. Phones and tablets are expected to be pre-configured for wireless debugging; see Settings > Devices > Scan Network.

## 11b. Android Power Tools (iframe of ws-scrcpy-web home page)

- [ ] Sidebar shows an "Android Power Tools" group between Android Devices and Jellyfin
- [ ] Clicking "Power Tools" navigates to `/android-power-tools`
- [ ] Breadcrumb in TopBar reads **"Android Power Tools"** (not "Android Devices" — page-title switch has a specific case above the `android` fallback)
- [ ] Full ws-scrcpy-web home page loads inside an iframe: device cards, Available Network Devices / Scan Network / Manually Add Device panel, Dependencies panel
- [ ] Iframe fills the content area below TopBar without introducing its own scrollbar (body scroll is locked, page-level scroll lives inside the iframe only when content actually overflows)
- [ ] If ws-scrcpy-web is not running: a "ws-scrcpy-web isn't running" warning alert is shown instead of a broken iframe
- [ ] Clicking `shell` on a device card opens the xterm modal inside the iframe; terminal is interactive (typing reaches the device, output renders)
- [ ] Clicking `list files` opens the file browser modal inside the iframe; sticky header stays pinned on scroll; hover icons scale with the size picker
- [ ] Clicking `config stream` opens ConfigureScrcpy; codec / encoder dropdowns filter correctly; Connect opens ConnectModal and the stream plays inside the iframe
- [ ] All modals' backdrops cover only the iframe viewport (not Control Menu's TopBar / sidebar — they remain usable)
- [ ] ws-scrcpy-web's own theme toggle is independent of Control Menu's (iframe has its own localStorage origin). Not a bug; just worth noting

## 12. Cameras > Camera View

- [ ] Navigate to `/cameras/1` with unconfigured camera — "Camera 1 not configured" message with link to settings
- [ ] Configure Camera 1 with name, IP, credentials — save, refresh, navigate to `/cameras/1`
- [ ] Configured camera shows iframe with RTSP stream via go2rtc
- [ ] If go2rtc is not running — "Streaming service unavailable" message
- [ ] Camera sidebar entries use camera emoji icon
- [ ] Configured cameras show custom names in sidebar (e.g., "Front Door")
- [ ] Default cameras show "Camera 1", "Camera 2", etc.

## 13. Cameras > go2rtc Service

- [ ] On app startup, go2rtc process starts (port 1984 in use)
- [ ] Only cameras with credentials appear in go2rtc.yaml
- [ ] Saving camera settings triggers go2rtc restart
- [ ] If go2rtc crashes — auto-restarts (up to 2 times in 30 seconds)
- [ ] If go2rtc crashes 3 times in 30s — gives up (check logs)

## 14. Jellyfin > DB Date Update

- [ ] Page loads, shows steps overview
- [ ] Click "Start Update":
  - [ ] Step 1: Container stops (shows truncated container ID)
  - [ ] Step 2: Backup created
  - [ ] Step 3: SQL update runs
  - [ ] Step 4: Container starts, **waits for "Startup complete"** (should now actually detect it via stderr)
  - [ ] Step 5: Old backups cleaned
  - [ ] All steps show green checkmarks on success
- [ ] If any step fails: error shows immediately (red X), **container is restarted** on failure
- [ ] Recent Operations table shows styled status badges

## 15. Jellyfin > Cast & Crew Update

- [ ] Page loads, shows info box
- [ ] Click "Start Update" — job starts, progress bar appears
- [ ] Click "Cancel" — job stops (verify via progress stopping and status changing to Failed)
- [ ] Navigate away during a running job, come back — job still shows progress (worker survived page navigation)
- [ ] Job history table shows completed/failed jobs
- [ ] If a previous job got stuck in "Running" state — it should now be clearable (fail on cancellation)

## 16. Dependency Version Management (ADB Update Fix)

- [ ] Settings > Dependencies: Check ADB — version appears (not "Not found" if installed locally)
- [ ] ADB shows correct local version, not a stale system PATH version
- [ ] If ADB update available: "Update" button resolves to a versioned URL (not `-latest-`)
- [ ] Node.js version check resolves (shows installed version)
- [ ] Node.js update URL resolves to versioned dist URL (not generic download page)
- [ ] After updating a dependency: no infinite update loop (status stays "Up to date")

## 17. Cast & Crew Email Notifications

- [ ] Set a notification email in Settings > General
- [ ] Run a Cast & Crew update — on completion, email is sent with summary
- [ ] Cancel a running Cast & Crew job — email is sent with cancellation notice
- [ ] If no notification email is set — no error, notification is silently skipped

## 18. Utilities > Icon Converter

- [ ] Upload a PNG image
- [ ] Select sizes, click Convert
- [ ] Download link appears — file downloads successfully
- [ ] UI is responsive during conversion (not frozen — async via Task.Run)

## 19. Utilities > File Unblocker (Windows only)

- [ ] Enter a valid directory path — files are unblocked, count shown
- [ ] Enter a non-existent path — "Directory not found" error message
- [ ] Path with spaces works correctly

## 20. TopBar

- [ ] Update badge (bell icon) hover has visible background change
- [ ] If dependency updates available: badge count shows

## 21. Navigation Edge Cases

- [ ] Navigate to `/settings/nonexistent` — shows "Unknown settings section" message with link
- [ ] Navigate to `/android/googletv` without a device — first Google TV device is selected (no crash)
- [ ] Navigate to `/android/phone` without a device — first Android Phone device is selected (no crash)

## 22. ws-scrcpy-web Integration

- [ ] If ws-scrcpy-web is configured and running: "Screen mirroring unavailable" does NOT show
- [ ] If ws-scrcpy-web crashes: mirror shows unavailable (not stale "running" state)
- [ ] Restart via code: service comes back online with readiness check
- [ ] **Stream quality refresh does not kill mouse input** (race condition fixed)
- [ ] **Offline devices do not crash ws-scrcpy-web** (WebSocket close reason truncated)

---

## Quick Smoke Test (5 min)

If you're short on time, just hit these:

1. [ ] App starts, home page shows hero + module cards with pill buttons
2. [ ] Theme toggle works, page title updates on navigation
3. [ ] Sidebar expand/collapse persists across refresh
4. [ ] Settings > General: SMTP fields save without reverting
5. [ ] Settings > Cameras: save a camera, verify name shows in sidebar after refresh
6. [ ] Settings > Dependencies: badges are styled, disabled buttons are dimmed
7. [ ] Jellyfin > DB Date Update: start a run, verify Step 4 detects "Startup complete"
8. [ ] Edit a device, cancel — verify original values unchanged
9. [ ] Google TV mirror: clicks work, survive stream refresh
10. [ ] Cast & Crew update sends email on completion (if notification email set)

## Settings Grid Redesign (2026-05-05)

### Sub-nav order

- [ ] `/settings` left rail order: General → Jellyfin → Android Devices → Cameras → Dependencies.

### General page

- [ ] General section row order: Re-run Setup Wizard, Timezone, Theme.
- [ ] Theme cell shows the hint "Also available from the icon in the top-right of every page."
- [ ] Theme buttons toggle theme immediately.
- [ ] Email (SMTP) renders as 4-row 2-col grid; per-field auto-save still works.
- [ ] Test Email button alone in the bottom row.
- [ ] ws-scrcpy-web URL field disabled in Managed mode.
- [ ] Switch to External → URL editable; blur saves with "Restart to apply" notification.

### Jellyfin page — non-migration sections

- [ ] Docker Compose parse-result table shows only Container Name + Database Path (no Backup Directory).
- [ ] Jellyfin API has a single bottom-right Save button (bi-floppy icon). Edit + Save persists all three fields (verify via reload).
- [ ] Cast & Crew Notifications has a single Save Notification Email button.

### Jellyfin page — Logging, Backup & Retention

- [ ] Section title reads "Logging, Backup & Retention".
- [ ] Three rows: Backups, Logs, Retention. Each row has its own Save button.
- [ ] Stats show file count + total size for Backups, file count for Logs.
- [ ] Backups path migration: change path to a new empty dir → existing `.db` files move; notification confirms count.
- [ ] Logs path migration: at least one log file likely locked → partial-success notification mentions the locked file by name.
- [ ] Retry-after-restart-or-rotation: re-clicking Save migrates remaining files.
- [ ] Retention save persists the new day count.

## External Dependencies Refactor (2026-05-05)

### General Settings — External Dependencies section

- [ ] Settings → General page has an **External Dependencies** section (was: "ws-scrcpy-web deployment").
- [ ] Section intro paragraph mentions scanning features need ws-scrcpy-web running.
- [ ] **No** Managed/External radio toggle.
- [ ] ws-scrcpy-web URL field is editable; blur saves with "Restart to apply" notification.
- [ ] Docker executable path field is editable; blur saves with "Docker path saved." notification (or "Docker path cleared." if emptied).

### Dependencies page

- [ ] docker row: Installed/Latest/Status/Install Path/Actions all visible (no colspan merge).
- [ ] docker row: Install Path column shows the configured path (or "Not configured (set in General Settings)") in muted text — **no editable input, no Reset button**.
- [ ] docker row: **No Update or Install button** — only Check + project-link.
- [ ] ws-scrcpy-web row: same shape — read-only Install Path showing `External: <url>`, no Update button.
- [ ] After configuring a valid Docker path on General Settings, the Dependencies page Docker row shows actual installed Docker version after Check (not `–`).

### Settings cleanup (idempotent on every boot)

- [ ] On first run after upgrade, the `wsscrcpy-mode` and `ws_scrcpy_web_path` (module=android-devices) settings rows are removed from the DB.
- [ ] Subsequent boots: cleanup is a no-op (verified via log: no "Removed obsolete setting…" lines after first run).
