# Fresh-VM smoke #1 runbook (Phase 1 ship gate)

**VM:** WIN11-CONTROL-MENU (Hyper-V, baseline snapshot per `user_test_devices.md`).

This runbook verifies the Phase 1 deliverable end-to-end: Velopack-installed
ControlMenu boots, persists user data outside `current\`, and the manual
update flow completes (alpha.1 -> alpha.2). No tray, no Servy, no signing
yet -- those are Phase 2-4.

**Pre-flight:**
- Roll VM back to baseline snapshot (clean Win11, no .NET runtime, no VC redists).
- Confirm test user is non-admin (so the ACL grant smoke fires UAC).
- Have two Setup.exe versions ready: `1.1.0-alpha.1` and `1.1.0-alpha.2` (a
  trivial change between them, e.g., a CHANGELOG bump).

---

## Steps

### 1. Build local alpha.1 Setup.exe on dev machine

```powershell
pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.1
```

Output: `Releases/ControlMenu-Setup.exe` (or similar Velopack naming).

Prerequisite check: `vpk` must be on PATH (`dotnet tool install -g Velopack.Vpk`
if not present). Verify with `vpk --version`.

### 2. Copy alpha.1 Setup.exe to VM

Transfer via Hyper-V shared folder, a network share, or a mounted ISO.
Place on the VM desktop for easy access.

### 3. Run Setup.exe on VM as non-admin user

- SmartScreen warning expected (unsigned local pack) -- "More info" -> "Run anyway".
- UAC prompt -> Accept (elevation for PerMachine install to `C:\Program Files\`).
- Velopack installs to `C:\Program Files\ControlMenu\`.
- Installer exits when complete; no manual "Finish" click required.

### 4. Verify on-disk layout post-install

Open an Explorer window or PowerShell prompt and confirm:

```
C:\Program Files\ControlMenu\ControlMenu.exe          (Velopack stub launcher)
C:\Program Files\ControlMenu\Update.exe               (Velopack update engine)
C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe
C:\Program Files\ControlMenu\current\ControlMenu.exe
C:\Program Files\ControlMenu\current\ControlMenuTray.exe  (Phase 1 stub)
```

Also confirm: `C:\ProgramData\ControlMenu\` does NOT yet exist (created on
first launch, not at install time).

### 5. Launch via Start Menu shortcut

- Open Start Menu, search "Control Menu", click the shortcut.
- **First launch only:** UAC prompt fires for the install-root ACL grant
  (`InstallAcl.cs`). Accept it.
- A console window opens (Phase 1; Phase 2 hides it via tray).
- The default browser auto-opens to `http://localhost:5159`. If it does not
  auto-open within 10 seconds, open it manually.

**Click through every page and confirm they load without errors:**
- Home
- Settings -> General
- Settings -> Devices
- Settings -> Cameras
- Settings -> Jellyfin
- Settings -> Email
- Settings -> Dependencies
- Setup Wizard
- Android Power Tools
- Cameras
- Jellyfin
- Utilities

**Verify data-root was created:**
```
C:\ProgramData\ControlMenu\config\controlmenu.db       (SQLite DB)
C:\ProgramData\ControlMenu\logs\launcher.log           (launcher log)
C:\ProgramData\ControlMenu\logs\controlmenu.log        (app log)
C:\ProgramData\ControlMenu\dependencies\               (populated by dep manager)
```

Open `launcher.log` and confirm the Phase 1 entry sequence is present (INSTALL
hook, ACL grant, single-instance check, child supervisor start) with no ERROR
lines.

### 6. Persist user data (survives-update checkpoint)

In the running app:
- Settings -> Cameras: add a fake camera (any label, IP like `192.168.1.99`).
- Settings -> Jellyfin: set a Compose path (e.g., `C:\docker\jellyfin`), save.

Note the exact values -- you will verify them again after the update in step 10.

### 7. Build alpha.2 with a trivial change on dev machine

On the dev machine:
1. Bump CHANGELOG.md: add a `[1.1.0-alpha.2]` section with "Smoke test bump".
2. Commit: `git commit -m 'chore: bump to 1.1.0-alpha.2 for smoke'`
3. Build:
   ```powershell
   pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.2
   ```

Output: `Releases/` now contains both alpha.1 and alpha.2 release assets plus
an updated `RELEASES.win.json` feed file.

### 8. Stage alpha.2 for the VM to consume

**Option A -- GitHub Releases (preferred for real smoke):**
Upload the full `Releases/` output to a GitHub Release on the CM repo. The
`VelopackUpdateService` points at the GitHub source already (Task 11).

```powershell
# Example using gh CLI (requires GITHUB_TOKEN)
gh release create v1.1.0-alpha.2 Releases/* --prerelease --title "1.1.0-alpha.2 smoke"
```

**Option B -- Local HTTP feed (faster iteration, no GitHub):**
Serve the `Releases/` folder via a local HTTP server accessible from the VM:

```powershell
# On dev machine:
python -m http.server 8765 --directory Releases/
```

Then temporarily change `VelopackUpdateService.cs` to point at
`http://<dev-machine-IP>:8765` instead of the GitHub source, rebuild, and
re-run step 1.

### 9. Trigger update check in the VM's running app

In the browser at `http://localhost:5159`:
1. Navigate to Settings -> General -> Updates.
2. Click "Check for updates".
3. An update-available banner shows `1.1.0-alpha.2`.
4. Click "Download and apply (app will restart)".

**Expected sequence (observable in `launcher.log`):**
- `VelopackUpdateService` downloads the delta/full package.
- App sends exit code 75 to the launcher (stop-for-update signal).
- Launcher logs pre-apply hygiene: daemon stop, wait, confirm.
- Launcher logs "Phase 3 lands apply orchestration" (Phase 1 stub; apply is
  deferred to Phase 3).
- Velopack's `Update.exe` applies the new package to `current\`.
- Launcher exits (no auto-relaunch in Phase 1).

**Phase 1 manual step:** The app does NOT auto-relaunch (that is Phase 2 via
tray / Phase 3 via Servy). Relaunch manually via the Start Menu shortcut.

### 10. Verify post-update state

After manual relaunch:

**Version check:**
- Open Settings -> General. The displayed version should be `1.1.0-alpha.2`.
- Or check file mtime: `C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe`
  mtime should be after the alpha.2 build time.

**User data survived:**
- Settings -> Cameras: the fake camera from step 6 is still there.
- Settings -> Jellyfin: the Compose path from step 6 is still there.
- `C:\ProgramData\ControlMenu\config\controlmenu.db` mtime predates the
  alpha.2 install (the DB was NOT replaced by the update).

---

## Pass criteria

All 10 steps complete without crashes or ERROR log entries.

Specifically:
- `launcher.log` and `controlmenu.log` contain only INFO-level entries for
  the full install + first-run + update-check + download + apply + relaunch
  sequence.
- `1.1.0-alpha.2` binaries are present in `current\` after manual relaunch.
- Camera entry and Jellyfin Compose path from step 6 are unchanged.
- `C:\ProgramData\ControlMenu\` was NOT touched by the Velopack update
  (user data lives outside `current\` by design -- the core Phase 1 invariant).

---

## Post-smoke snapshot

After a clean pass: take a Hyper-V snapshot named `post-smoke-1`. This is the
baseline for Phase 2 (tray, auto-relaunch, Servy integration) testing.

---

## On failure

**On first failure:** STOP. Do not retry immediately.

1. Take a Hyper-V snapshot named `smoke-1-fail-<timestamp>` to preserve VM state.
2. Copy logs off the VM:
   ```
   C:\ProgramData\ControlMenu\logs\launcher.log
   C:\ProgramData\ControlMenu\logs\controlmenu.log
   ```
   Save to `<repo>/scratch/smoke-1-fail-<timestamp>/` on the dev machine.
3. Roll the VM back to the baseline snapshot.
4. Diagnose on dev. Fix on `feature/velopack-phase-1`. Re-run from step 1.

**If 3+ retries fail with different symptoms each time:** halt and surface to
user. This pattern suggests an architectural issue (path resolver, hook
ordering, install-root ACL timing) that requires spec amendment before
continuing.
