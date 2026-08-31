# Control Menu — Manual Test Checklist

Post-audit verification. Run the app with `dotnet run` from `src/ControlMenu/`.

---

## 1. Startup & Home Page

- [ ] App starts without errors in console
- [ ] Home page loads with header band: brand icon (small, top-left) + "Control Menu" title + status line + three Quick Scan buttons (Scan Android / Scan Cameras / Scan All) at fixed width on the right
- [ ] Cold state status line reads "Find devices and cameras on your network, then manage them."
- [ ] DISCOVERED — ANDROID and DISCOVERED — CAMERAS sections render below the header with outlined dashed-border placeholder cards when no devices are discovered
- [ ] Each placeholder card prompts the user to run a scan (Android: "No Android devices found on the last scan. Run a scan to look for Androids on your network."; Cameras: "No new cameras discovered. Run a scan to look for cameras on your network.")
- [ ] Module-tile nav row pinned to bottom of viewport: Android Devices · Power Tools · Jellyfin · Cameras · Imaging Tools · Utilities · Settings
- [ ] Each module tile routes to its module's first nav entry (e.g., Android Devices → /android/devices, Imaging Tools → /imaging/icon-converter, Settings → /settings/general); Cameras tile falls back to /settings/cameras when no cameras are registered
- [ ] Click Scan Android — only that button shows ⏳ Scanning Android…; on completion the Discovered — Android section populates (or shows the empty placeholder if nothing found); status-line "last scan Xs ago" updates
- [ ] Click Scan Cameras — symmetric behavior; if subnet detection fails, an inline error renders in the header band and Cameras section does NOT mark itself scanned
- [ ] Click Scan All — all three buttons enter Running state simultaneously; Android + Cameras scan in parallel; both Discovered sections populate on completion
- [ ] Scan All while Android scan already running — Scan All immediately enters Running state; Cameras kicks off in tandem; Android scan continues uninterrupted
- [ ] Inline + Add a Discovered Android device → row disappears from Discovered, status line registered count bumps
- [ ] Inline + Add a Discovered Camera → same behavior via the embedded DiscoveredCamerasPanel
- [ ] Cameras count badge reflects FILTERED hits (not raw `Hits.Count`): when all hits are already-registered, badge shows 0 and outlined placeholder card renders (not the panel's own bare empty message)
- [ ] "No modules loaded" message does NOT show (verifies module discovery works)
- [ ] Setup-wizard guard: if `setup-completed` setting is cleared, Home renders the SetupWizard exclusively (none of the dashboard surfaces visible)

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
- [ ] In the Configured table, click ✎ on a camera. Edit modal opens. Rename + save; verify the new name appears in the table AND in the left-nav sidebar entry within seconds (no manual refresh).
- [ ] Toggle the Enabled checkbox off on a camera. Verify the camera's left-nav sidebar entry disappears within seconds (go2rtc regenerates config; sidebar subscribes to ICameraChangeNotifier).
- [ ] Toggle Enabled back on. Verify the sidebar entry reappears within seconds.
- [ ] Open Control Menu in a second browser tab. Add/delete/enable-toggle/rename a camera in tab #1; verify tab #2's sidebar updates without requiring its own refresh (cross-circuit notifier propagation).
- [ ] Delete a camera. Verify it's gone from the table; no orphan secret rows in the DB.
- [ ] Set `cameras-liveness-interval-seconds` to `300` (minimum); wait for a liveness tick; verify enabled cameras' Status/Last Seen refresh. Set to `0`; verify liveness probing stops. (The old periodic subnet auto-scan was replaced by per-camera liveness in v1.0.0 — there is no longer an automatic Discovered-panel scan.)
- [ ] Add a subnet via the AddSubnetModal; verify next scan covers it.
- [ ] Close the camera scan modal while a scan is running. Verify the modal closes, the scan continues in the background, and partial Hits accumulate as discovered.

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
- [ ] Step 3 (Cameras): "Scan Network" auto-detects the LAN subnet and runs an ONVIF-only scan; discovered cameras stream into the DiscoveredCamerasPanel
- [ ] Step 3: inline-Add a discovered camera (optional shared credentials above the grid) — it moves into the registered-cameras summary table
- [ ] Step 3: click Back to Step 2, then forward — added cameras persist
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
- [ ] If ws-scrcpy-web is not reachable: an HTTP probe runs on page init, the iframe is suppressed, and a warning alert is shown ("ws-scrcpy-web isn't reachable at <url>") with a Re-check button. Clicking Re-check re-runs the probe.
- [ ] Clicking `shell` on a device card opens the xterm modal inside the iframe; terminal is interactive (typing reaches the device, output renders)
- [ ] Clicking `list files` opens the file browser modal inside the iframe; sticky header stays pinned on scroll; hover icons scale with the size picker
- [ ] Clicking `config stream` opens ConfigureScrcpy; codec / encoder dropdowns filter correctly; Connect opens ConnectModal and the stream plays inside the iframe
- [ ] All modals' backdrops cover only the iframe viewport (not Control Menu's TopBar / sidebar — they remain usable)
- [ ] ws-scrcpy-web's theme syncs bidirectionally with Control Menu via the iframe theme bridge. Toggle CM's theme — iframe content + ws-scrcpy-web's own toggle icon flip in sync. Toggle inside the iframe — CM's TopBar theme icon flips too. Reload either side — both adopt CM's persisted theme.

## 12. Cameras > Camera View (post-scanner architecture)

- [ ] Configured cameras live in the `Cameras` DB table; each gets a sidebar nav entry with the user-assigned name and the bespoke 3D-style camera SVG icon.
- [ ] Navigate to `/cameras/{guid}` for a registered camera — page renders the go2rtc-proxied stream inside an iframe.
- [ ] Navigate to a deleted camera's URL — "Camera not found" message with a link back to Settings → Cameras.
- [ ] Add a camera via Settings → Cameras → Add Camera Manually (IP and Port fields editable; full RTSP path entry).
- [ ] Add a camera via the toolbar Quick Scan / Scan Network → discovered ONVIF row → inline credentials → Add. Camera moves from Discovered into Configured.
- [ ] Add a camera via the Home page Discovered Cameras scanner — same flow as Settings.
- [ ] Add a camera via the Setup Wizard's Cameras step — registers correctly and shows in the wizard's summary table at the bottom.
- [ ] In all four add paths, the new camera's sidebar entry appears within seconds without manual refresh (cross-circuit if multiple tabs are open).
- [ ] Edit a camera's name → sidebar label updates within seconds.
- [ ] Toggle Enabled off → sidebar entry disappears; toggle back on → entry reappears.
- [ ] Delete a camera → sidebar entry disappears; go2rtc.yaml drops the stream; navigating to the old URL shows "Camera not found".
- [ ] If go2rtc is not running — Camera View page shows "Streaming service unavailable" message.

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
  - [ ] Step 4: Container starts, then **waits for Jellyfin to be ready** — the container healthcheck reporting `healthy`, or `Startup complete` in logs timestamped *after* this start. Budget 120s. A failure here means "started but never reported ready", which is NOT `docker start` failing — the step text distinguishes the two
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

> **A run that processes thousands of people and adds almost no images is the expected result — do not
> file it as a bug.** The job now genuinely asks Jellyfin to fetch (`POST /Items/{id}/Refresh`; it
> previously issued only a `GET`, which fetched nothing). But measured against a real library, TMDb had
> a profile photo for **0 of 6,545** in-media people missing one, and Jellyfin's own `/RemoteImages`
> returned nothing for **0 of 309** sampled — against a control group of known actors that returned 10
> images each. The people without images are overwhelmingly guest stars and crew with no catalogued
> headshot anywhere. This job matters for **new** media arriving with photographed cast.
>
> To test the *mechanism* rather than the yield: pick one person the job processed and check
> `GET /Items/{id}/RemoteImages?type=Primary`. If it returns candidates, the job should have downloaded
> one; if it returns none, there was nothing to fetch and the job behaved correctly.

## 16. Dependency Version Management (ADB Update Fix)

- [ ] Settings > Dependencies: Check ADB — version appears (not "Not found" if installed locally)
- [ ] ADB shows correct local version, not a stale system PATH version
- [ ] If ADB update available: "Update" button resolves to a versioned URL (not `-latest-`)
- [ ] After updating a dependency: no infinite update loop (status stays "Up to date")
- [ ] Neither Node.js nor scrcpy appears in the dependency list (node removed in v1.0.0; the orphaned scrcpy dependency was later dropped — CM invokes neither directly).
- [ ] The Imaging Tools deps appear and resolve locally: **magick** (GitHub source), **vtracer** (GitHub), **potrace** (DirectUrl) — each shows a local version after Check, not "Not found" (they're pre-seeded into the MSI). magick's Check parses `ImageMagick 7.1.2-25`; vtracer's parses `VTracer 0.6.4`; potrace's parses `potrace 1.16`.

## 17. Cast & Crew Email Notifications

- [ ] Set a notification email in Settings > General
- [ ] Run a Cast & Crew update — on completion, email is sent with summary
- [ ] Cancel a running Cast & Crew job — email is sent with cancellation notice
- [ ] If no notification email is set — no error, notification is silently skipped

## 18. Imaging Tools (magick + Svg.Skia + vtracer + potrace)

Top-level sidebar section (after Cameras). All six tools accept an image via drag-and-drop or the File System Access API picker (Chrome/Edge) and save via the browser download / native save dialog. magick / vtracer / potrace are bundled (auto-installed); verify they show **Found** in Settings → Dependencies first.

### 18a. Sidebar + routing

- [ ] Sidebar shows an "Imaging Tools" group after Cameras with six entries: Icon Converter, Format Converter, Image Resize, SVG Rasterize, Magic Wand, Tracing
- [ ] Navigating to each entry updates the TopBar breadcrumb to the matching tool name (Icon Converter / Format Converter / Image Resize / SVG Rasterize / Magic Wand / Tracing)
- [ ] **Legacy redirect:** open `/utilities/icon-converter` directly in the URL bar → it `replace`-redirects to `/imaging/icon-converter` (no back-button trap)

### 18b. Icon Converter (`/imaging/icon-converter`)

- [ ] Upload a PNG image; select sizes; click Convert
- [ ] Download link appears — `.ico` downloads successfully and opens as a valid multi-size icon
- [ ] UI stays responsive during conversion (not frozen)

### 18c. Format Converter (`/imaging/format-converter`)

- [ ] Source readout shows width × height / format / alpha after picking an image
- [ ] Target-format dropdown lists exactly: PNG, JPG, WEBP, AVIF, TIFF, BMP, GIF (no HEIC)
- [ ] Quality slider appears only for lossy targets (JPG / WEBP / AVIF), hidden for PNG / TIFF / BMP / GIF
- [ ] Convert PNG → WEBP and PNG → AVIF; output downloads and opens correctly
- [ ] Output filename uses the chosen extension

### 18d. Image Resize (`/imaging/image-resize`)

- [ ] Original-dimensions readout shows after picking an image
- [ ] Pixel mode: set explicit width/height, resize, verify output dimensions
- [ ] Percentage mode: e.g. 50%, verify halved dimensions
- [ ] Max-dimension-fit mode: longest side capped, aspect ratio preserved
- [ ] Output preserves the source format; filename is `<base>-resized.<ext>`

### 18e. SVG Rasterize (`/imaging/svg-rasterize`)

- [ ] Pick an `.svg`; rasterize to PNG (rendered in-process via Svg.Skia, no magick)
- [ ] Output PNG dimensions match the requested size; transparency preserved
- [ ] Rasterize to ICO option produces a valid icon

### 18f. Magic Wand — background removal (`/imaging/magic-wand`)

- [ ] Pick an image with a clean background; click a background pixel to seed
- [ ] Drag the tolerance slider — the **preview** updates live per change (in-process SkiaSharp flood-fill; snappy even on a large image because the preview is downscaled to ≤800px longest side)
- [ ] Toggle Contiguous vs Global — Contiguous clears only the region connected to the seed; Global clears every matching pixel
- [ ] Click Apply — the **saved** PNG is rendered authoritatively by magick (may differ slightly from the preview) with the background transparent
- [ ] Save the result; reopen — background is transparent

### 18g. Tracing — raster → SVG (`/imaging/tracing`)

- [ ] Color mode: trace a colorful raster via vtracer; output SVG opens and resembles the source
- [ ] Tune vtracer knobs (mode pixel/polygon/spline, filter speckle, color precision) — output reflects the change
- [ ] Monochrome mode: trace a black-and-white raster via potrace (magick first makes a bilevel BMP, then potrace traces it); output is a clean single-color SVG
- [ ] The "Open in svgedit" button is present but **always disabled** with a "Coming soon" tooltip (placeholder for a future task)
- [ ] If a tracer binary is missing, a clear error surfaces (not a silent failure)

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
- [ ] ws-scrcpy-web is external — CM does not spawn or restart it; after you restart your own ws-scrcpy-web instance, the mirror reflects reachability via the HTTP probe (`ProbeAsync`), with no CM-side readiness wait
- [ ] **Stream quality refresh does not kill mouse input** (race condition fixed)
- [ ] **Offline devices do not crash ws-scrcpy-web** (WebSocket close reason truncated)

---

## Quick Smoke Test (5 min)

If you're short on time, just hit these:

1. [ ] App starts, home page shows the discovery dashboard (header band + Quick Scan buttons + Discovered sections + module-tile nav)
2. [ ] Theme toggle works, page title updates on navigation
3. [ ] Sidebar expand/collapse persists across refresh
4. [ ] Settings > General: SMTP fields save without reverting
5. [ ] Settings > Cameras: save a camera, verify name shows in sidebar after refresh
6. [ ] Settings > Dependencies: badges are styled, disabled buttons are dimmed
7. [ ] Jellyfin > DB Date Update: start a run, verify Step 4 reports Jellyfin online (healthcheck or post-start log marker), not a false "failed to start"
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
- [ ] ws-scrcpy-web URL field is editable; blur saves with "ws-scrcpy-web URL saved." (no restart — the Power Tools page re-reads the setting on each visit).
- [ ] A value with no scheme (e.g. `localhost:8000`) is rejected with "Enter a full URL including http:// or https:// — for example http://localhost:8000".
- [ ] A pasted trailing slash is stripped before storing.
- [ ] Docker executable path field is editable; blur saves with "Docker path saved." notification (or "Docker path cleared." when emptied).
- [ ] Hover over any settings row — background-color transition is subtle (~280ms ease-out, partial opacity); no abrupt color flip.

### Jellyfin page — non-migration sections

- [ ] Docker Compose parse-result table shows only Container Name + Database Path (no Backup Directory).
- [ ] Save & Parse button under Docker Compose persists the path AND reparses YAML (compound action; only explicit save button on the page).
- [ ] Jellyfin API fields (URL, key, user ID) auto-save on blur; reload page to verify all three persisted.
- [ ] Cast & Crew notification email auto-saves on blur; reload to verify.

### Jellyfin page — Logging, Backup & Retention

- [ ] Section title reads "Logging, Backup & Retention".
- [ ] Three rows: Backups path, Logs path, Retention day count. Path/value changes auto-save on blur.
- [ ] Stats show file count + total size for Backups, file count for Logs.
- [ ] Backups path migration: change path to a new empty dir → existing `.db` files move; notification confirms count.
- [ ] Logs path migration: at least one log file likely locked → partial-success notification mentions the locked file by name.
- [ ] Retry-after-restart-or-rotation: re-blur the path field to migrate remaining files.
- [ ] Retention day count persists on blur.

## External Dependencies Refactor (2026-05-05)

### General Settings — External Dependencies section

- [ ] Settings → General page has an **External Dependencies** section (was: "ws-scrcpy-web deployment").
- [ ] Section intro paragraph mentions scanning features need ws-scrcpy-web running.
- [ ] **No** Managed/External radio toggle.
- [ ] ws-scrcpy-web URL field is editable; blur saves with "ws-scrcpy-web URL saved." (no restart — the Power Tools page re-reads the setting on each visit).
- [ ] A value with no scheme (e.g. `localhost:8000`) is rejected with "Enter a full URL including http:// or https:// — for example http://localhost:8000".
- [ ] A pasted trailing slash is stripped before storing.
- [ ] Docker executable path field is editable; blur saves with "Docker path saved." notification (or "Docker path cleared." if emptied).

### Dependencies page

- [ ] docker row: Installed/Latest/Status/Install Path/Actions all visible (no colspan merge).
- [ ] docker row: Install Path column shows the configured path (or "Not configured (set in General Settings)") in muted text — **no editable input, no Reset button**.
- [ ] docker row: **No Update or Install button** — only Check + project-link.
- [ ] ws-scrcpy-web row: same shape — read-only Install Path showing `External: <url>`, no Update button.
- [ ] After configuring a valid Docker path on General Settings, the Dependencies page Docker row shows actual installed Docker version after Check (not `–`).

## Sidebar Fly-out (2026-05-05)

- [ ] Collapse the sidebar via the `<` toggle.
- [ ] Click any section icon (Android Devices / Cameras / Jellyfin / etc.) → fly-out panel opens to the right of the icon, vertically aligned with it.
- [ ] Panel header shows module name + icon. Below it, nav entries match what the expanded sidebar shows for that module.
- [ ] Click a different section icon → previous fly-out closes; new one opens.
- [ ] Click a link inside the fly-out → navigates and closes.
- [ ] Click outside the fly-out (on the page background, main content, or another icon) → closes.
- [ ] Press Esc with the fly-out open → closes.
- [ ] Toggle sidebar back to expanded while fly-out is open → fly-out closes.
- [ ] Resize browser window while fly-out is open → fly-out closes (acceptable v1 behavior).
- [ ] Active route highlight survives inside the fly-out (current page is highlighted in the fly-out's link list).
- [ ] Animation: 200ms slide-and-fade-in on open; not twitchy on rapid icon clicks.
- [ ] Empty module (zero visible nav entries) — clicking its icon when collapsed is a no-op (no empty panel).
