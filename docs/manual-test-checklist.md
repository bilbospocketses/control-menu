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

- [ ] Existing devices appear in the table
- [ ] Click "Add Device" — form appears
- [ ] Fill form, click Save — device appears in table
- [ ] Click "Edit" on a device — form populates with device data
- [ ] **Edit a field, click Cancel** — original value is unchanged in the table (not mutated)
- [ ] Edit a field, click Save — change is persisted
- [ ] Delete a device — removed from table
- [ ] For Android Phone devices: "Screen Lock PIN" field appears (password type, stored encrypted)

## 6. Settings > Cameras

- [ ] Page loads at `/settings/cameras` with camera slots (default 8)
- [ ] Each camera slot has: Name, IP Address, RTSP Port (default 554), Username, Password
- [ ] Fill Camera 1: name + IP + credentials, click "Save Camera 1" — "Saved" appears
- [ ] Reload page — saved values restored
- [ ] Save camera with name + IP but no credentials — config saves, camera not added to go2rtc
- [ ] Change camera count, save — "Saved — refresh page to update sidebar" message
- [ ] Refresh page — sidebar shows correct number of camera entries
- [ ] Camera names appear in sidebar (e.g., "Front Door" instead of "Camera 1")
- [ ] Password field obscures input

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
- [ ] **Mouse clicks in mirror control the TV** (left-click = tap, right-click = back, middle = home)
- [ ] **Clicks continue working after quality protection stream refresh** (no dead clicks)
- [ ] Navigate away and back — no console errors about disposed components

## 11. Android > Android Phone Dashboard

- [ ] Page loads
- [ ] "Reset ADB Port" uses the device's configured port (check the status message)
- [ ] Connect/disconnect works
- [ ] Screen mirror loads in portrait orientation
- [ ] Mirror panel sizes dynamically from actual device screen dimensions (no black bars)
- [ ] **USB Setup Wizard:** "Enable Wireless ADB" button opens wizard
  - [ ] Step 1: "I've Connected" button, detects USB device
  - [ ] Step 2: runs `adb tcpip`, shows spinner, then success
  - [ ] Step 3: "Disconnect USB cable", resolves IP from MAC or shows manual IP field
  - [ ] Step 3: "Connect Wirelessly" completes the setup
- [ ] **Phone Unlock:** If PIN configured, "Unlock" button sends PIN via ADB
- [ ] **Phone Unlock:** If no PIN, shows "Set PIN in Settings" link

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
