# Control Menu — Manual Test Checklist

Post-audit verification. Run the app with `dotnet run` from `src/ControlMenu/`.

---

## 1. Startup & Home Page

- [ ] App starts without errors in console
- [ ] Home page loads, shows module cards (Android Devices, Jellyfin, Utilities)
- [ ] "No modules loaded" message does NOT show (verifies module discovery works)
- [ ] No "Phase 2+" text visible anywhere

## 2. Theme & Layout

- [ ] Dark mode is the default
- [ ] Toggle to light mode — all text is readable, no white-on-white
- [ ] Toggle back to dark — no elements stuck in light colors
- [ ] Theme persists after page refresh (stored in localStorage)
- [ ] Sidebar pill buttons are styled, not plain blue links
- [ ] Emoji icons show in sidebar (not Bootstrap Icons)
- [ ] **Page title in TopBar updates** when navigating to different pages (not stuck on "Home")

## 3. Settings > General

- [ ] Theme toggle works from settings
- [ ] SMTP server field: type a value, tab out — "Saved." appears, **value stays** (does not revert)
- [ ] SMTP port field: same test — value persists after save
- [ ] SMTP username: same test
- [ ] SMTP password: type a value, tab out — "Saved." appears
- [ ] SMTP password: clear the field, tab out — "Password cleared." appears
- [ ] Notification email: type and save, value persists
- [ ] "Send Test Email" button — shows result (success or failure message)
- [ ] "Re-run Setup Wizard" button — navigates to wizard

## 4. Settings > Devices

- [ ] Existing devices appear in the table
- [ ] Click "Add Device" — form appears
- [ ] Fill form, click Save — device appears in table
- [ ] Click "Edit" on a device — form populates with device data
- [ ] **Edit a field, click Cancel** — original value is unchanged in the table (not mutated)
- [ ] Edit a field, click Save — change is persisted
- [ ] Delete a device — removed from table

## 5. Settings > Jellyfin

- [ ] Compose file path + Parse button works (if docker-compose.yml exists)
- [ ] Container name and DB path auto-populate from compose parse
- [ ] API key field: set value, tab out — saved
- [ ] API key field: clear value, tab out — "API key cleared." message
- [ ] Base URL, User ID, Cast/Crew email — save and persist correctly
- [ ] Backup retention setting saves
- [ ] Managed directories section shows stats (if dirs exist)

## 6. Settings > Dependencies

- [ ] Dependencies table loads with status badges
- [ ] **Status badges are styled** (colored pills, not plain text)
- [ ] "Check All" button runs checks, badges update
- [ ] If an update is available: "Update" button appears
- [ ] **Update dialog** has proper overlay + centered layout (not unstyled)
- [ ] **Disabled buttons** appear visually dimmed (not identical to enabled)

## 7. Setup Wizard (re-run from Settings > General)

- [ ] Step 1 (Welcome) loads
- [ ] Step 2 (Devices): Add a device, advance to Step 3, **click Back** — device still visible in table
- [ ] Step 3 (Services): defaults show as "configured" count
- [ ] Step 4 (Dependencies): scan runs, found items show green "Found" badges
- [ ] Step 4: For any "Not Found" item — click "Enter Path...", enter a valid path, click OK — validates and shows version
- [ ] Step 5 (Done) — shows summary, "Finish" completes wizard

## 8. Android > Google TV Dashboard

- [ ] Page loads without errors
- [ ] If device connected: controls appear, power status dot is colored
- [ ] Screensaver shows actual state (not always "Google" — may show "Unknown" if disconnected)
- [ ] Status messages appear and auto-dismiss after 5 seconds
- [ ] Screen mirror iframe loads (if ws-scrcpy-web is configured)
- [ ] Navigate away and back — no console errors about disposed components

## 9. Android > Pixel Dashboard

- [ ] Page loads
- [ ] "Reset ADB Port" uses the device's configured port (check the status message)
- [ ] Connect/disconnect works
- [ ] Screen mirror loads

## 10. Jellyfin > DB Date Update

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

## 11. Jellyfin > Cast & Crew Update

- [ ] Page loads, shows info box
- [ ] Click "Start Update" — job starts, progress bar appears
- [ ] Click "Cancel" — job stops (verify via progress stopping and status changing to Failed)
- [ ] Navigate away during a running job, come back — job still shows progress (worker survived page navigation)
- [ ] Job history table shows completed/failed jobs
- [ ] If a previous job got stuck in "Running" state — it should now be clearable (fail on cancellation)

## 12. Dependency Version Management (ADB Update Fix)

- [ ] Settings > Dependencies: Check ADB — version appears (not "Not found" if installed locally)
- [ ] ADB shows correct local version, not a stale system PATH version
- [ ] If ADB update available: "Update" button resolves to a versioned URL (not `-latest-`)
- [ ] Node.js version check resolves (shows installed version)
- [ ] Node.js update URL resolves to versioned dist URL (not generic download page)
- [ ] After updating a dependency: no infinite update loop (status stays "Up to date")

## 13. Cast & Crew Email Notifications

- [ ] Set a notification email in Settings > General
- [ ] Run a Cast & Crew update — on completion, email is sent with summary
- [ ] Cancel a running Cast & Crew job — email is sent with cancellation notice
- [ ] If no notification email is set — no error, notification is silently skipped

## 14. Utilities > Icon Converter

- [ ] Upload a PNG image
- [ ] Select sizes, click Convert
- [ ] Download link appears — file downloads successfully
- [ ] UI is responsive during conversion (not frozen — async via Task.Run)

## 15. Utilities > File Unblocker (Windows only)

- [ ] Enter a valid directory path — files are unblocked, count shown
- [ ] Enter a non-existent path — "Directory not found" error message
- [ ] Path with spaces works correctly

## 16. TopBar

- [ ] Update badge (bell icon) hover has visible background change
- [ ] If dependency updates available: badge count shows

## 17. Navigation Edge Cases

- [ ] Navigate to `/settings/nonexistent` — shows "Unknown settings section" message with link
- [ ] Navigate to `/android/googletv` without a device — first Google TV device is selected (no crash)
- [ ] Navigate to `/android/pixel` without a device — first Pixel device is selected (no crash)

## 18. ws-scrcpy-web Integration

- [ ] If ws-scrcpy-web is configured and running: "Screen mirroring unavailable" does NOT show
- [ ] If ws-scrcpy-web crashes: mirror shows unavailable (not stale "running" state)
- [ ] Restart via code: service comes back online with readiness check

---

## Quick Smoke Test (5 min)

If you're short on time, just hit these:

1. [ ] App starts, home page shows modules
2. [ ] Theme toggle works, page title updates on navigation
3. [ ] Settings > General: SMTP fields save without reverting
4. [ ] Settings > Dependencies: badges are styled, disabled buttons are dimmed
5. [ ] Jellyfin > DB Date Update: start a run, verify Step 4 detects "Startup complete"
6. [ ] Edit a device, cancel — verify original values unchanged
7. [ ] Settings > Dependencies: ADB check shows local version, no update loop
8. [ ] Cast & Crew update sends email on completion (if notification email set)
