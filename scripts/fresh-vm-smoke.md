# Fresh-VM smoke #1 runbook (Phase 1 ship gate)

**VM:** WIN11-CONTROL-MENU (Hyper-V, baseline snapshot per `user_test_devices.md`).

This runbook verifies the Phase 1 deliverable end-to-end: Velopack-installed
ControlMenu boots, persists user data outside `current\`, and the manual
update flow completes (alpha.1 -> alpha.2). No tray, no Servy, no signing
yet -- those are Phase 2-4.

**One-time dev-machine prerequisite:**

The `local-pack.ps1` script auto-vendors .NET 10 SDK to `scripts/dependencies/dotnet/<version>/` on first run. The bootstrap requires:
- Internet access (downloads from `dot.net/v1/...`)
- ~700MB free disk for the vendored SDK

`vpk` is installed globally via `dotnet tool install -g vpk --version 0.0.1589-ga2c5a97` (matching ws-scrcpy-web's pattern; not vendored). The script auto-installs the right version if absent.

After first successful pack, subsequent runs are offline-capable.

---

**Machine ownership at a glance:**

| Step | Machine | Action |
|---|---|---|
| 1 | [DEV BOX] | Build alpha.1 |
| 2 | [DEV BOX → VM] | Transfer alpha.1 to VM |
| 3-6 | [VM] | Install, launch, persist user data |
| 7 | [DEV BOX] | Build alpha.2 |
| 8 | [DEV BOX] | Stage alpha.2 (GitHub Release or local HTTP) |
| 9-10 | [VM] | Trigger update, verify post-update |
| Post-smoke | [DEV BOX] | Take Hyper-V snapshot |

---

**Pre-flight (one-time VM setup, if baseline snapshot doesn't already have it):**

1. Roll VM back to baseline snapshot:
   ```powershell
   # On dev machine:
   Restore-VMSnapshot -VMName 'WIN11-CONTROL-MENU' -Name 'baseline' -Confirm:$false
   Start-VM -VMName 'WIN11-CONTROL-MENU'
   ```
2. Sign into the VM. If the baseline doesn't already have a non-admin test
   user, create one:
   - Settings → Accounts → Other users → Add account → "I don't have this
     person's sign-in information" → "Add a user without a Microsoft account".
   - Username: `cm-test`, password: anything memorable.
   - DEFAULT account type is Standard (non-admin) — that's what we want.
     Do NOT promote to admin.
3. Sign out of the admin account, sign into `cm-test`.
4. Confirm `cm-test` is non-admin: open Settings → Accounts → Your info —
   should NOT say "Administrator" below the username.
5. Have two version strings memorized: `1.1.0-alpha.1` and `1.1.0-alpha.2`.

If the baseline already has a non-admin `cm-test` (or similar) user, skip
steps 2-4.

---

## Steps

### 1. [DEV BOX] Build local alpha.1 Setup.msi

```powershell
pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.1
```

Output: `Releases/ControlMenu-<version>-Setup.msi` (plus delta/full nupkgs and RELEASES.win.json feed).

Prerequisite check: `local-pack.ps1` auto-installs the pinned vpk version
(`0.0.1589-ga2c5a97`) globally if absent. Verify post-run with `vpk --version`.

### 2. [DEV BOX → VM] Copy alpha.1 Setup.msi to VM

This is the only physical-transfer step. After this, alpha.1 lives on the
VM and dev-box involvement pauses until step 7.

**Option A (Recommended) — Hyper-V Enhanced Session drive redirection:**

1. From Hyper-V Manager, connect to the VM.
2. In the "Connect to <VM>" dialog, before clicking Connect, click
   "Show Options" → "Local Resources" → "More..." → check the drive
   containing your `Releases/` folder (typically `C:`).
3. Click Connect with Enhanced Session enabled. Your local drive appears in
   the VM's File Explorer under `\\tsclient\<drive>\` (RDP-style redirection).
4. Inside the VM, navigate to:
   ```
   \\tsclient\C\Users\jscha\source\repos\control-menu\Releases\
   ```
   (adjust path to wherever your repo lives), then copy the MSI to
   `C:\Users\cm-test\Desktop\` on the VM.

**Option B (Fallback) — SMB share on dev machine:**

1. On dev machine (run as admin, one-time; share persists across reboots):
   ```powershell
   New-SmbShare -Name "ControlMenuReleases" `
     -Path "C:\Users\jscha\source\repos\control-menu\Releases" `
     -ReadAccess "Everyone"
   ```
2. Find the dev machine's IP on the Hyper-V switch:
   ```powershell
   Get-NetIPAddress -InterfaceAlias 'vEthernet (Default Switch)' `
     -AddressFamily IPv4 | Select-Object IPAddress
   ```
   For Default Switch (NAT), the host typically appears to the VM as `172.x.x.1`.
3. On VM: open File Explorer, type `\\<DEV-MACHINE-IP>\ControlMenuReleases`
   in the address bar. Enter dev-machine credentials when prompted.
4. Copy the MSI from the share to `C:\Users\cm-test\Desktop\` on the VM.

### 3. [VM] Run Setup.msi on VM as non-admin user

- SmartScreen warning expected (unsigned local pack) -- "More info" -> "Run anyway".
- UAC prompt -> Accept (elevation for PerMachine install to `C:\Program Files\`).
- Velopack installs to `C:\Program Files\ControlMenu\`.
- Installer exits when complete; no manual "Finish" click required.

### 4. [VM] Verify on-disk layout post-install

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

### 5. [VM] Launch via Start Menu shortcut

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

### 6. [VM] Persist user data (survives-update checkpoint)

In the running app:
- Settings -> Cameras: add a fake camera (any label, IP like `192.168.1.99`).
- Settings -> Jellyfin: set a Compose path (e.g., `C:\docker\jellyfin`), save.

Note the exact values -- you will verify them again after the update in step 10.

### 7. [DEV BOX] Build alpha.2 with a trivial change

1. Bump CHANGELOG.md: add a `[1.1.0-alpha.2]` section with "Smoke test bump".
2. Commit: `git commit -m 'chore: bump to 1.1.0-alpha.2 for smoke'`
3. Build:
   ```powershell
   pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.2
   ```

Output: `Releases/` now contains both alpha.1 and alpha.2 release assets plus
an updated `RELEASES.win.json` feed file.

### 8. [DEV BOX] Stage alpha.2 for the VM to consume

**Option A (Recommended) — GitHub Release on the CM repo:**

The `VelopackUpdateService` points at the CM GitHub repo via GithubSource.
To let the VM's running app see the alpha.2 update:

1. Push the alpha.2 tag to origin:
   ```powershell
   git tag v1.1.0-alpha.2
   git push origin v1.1.0-alpha.2
   ```
2. Create the GitHub Release with the full `Releases/` artifacts attached.
   **CRITICAL: do NOT set `--prerelease`** — Velopack's GithubSource queries
   `/releases/latest` which excludes prereleases (Gotcha 5 in
   `feedback_velopack_permachine_lessons.md`). Channel separation is handled
   by the per-channel `releases.<channel>.json` feed file already in
   `Releases/`; the prerelease flag is redundant gating that ALSO breaks
   discovery.
   ```powershell
   gh release create v1.1.0-alpha.2 Releases/* --title "1.1.0-alpha.2 smoke"
   ```
   (No `--prerelease`.)

   **VM network requirement:** The VM must have internet access to reach
   `github.com` and download the release assets. Hyper-V Default Switch
   (NAT) provides this by default.

**Option B (Fallback) — Local HTTP feed, requires alpha.1 pre-modification:**

Local HTTP is faster to iterate but has a chicken-and-egg: the running
alpha.1 must already be pointing at the local HTTP feed (not GitHub) for
the check-for-updates flow to discover alpha.2 there. This means you'd
need to:

1. BEFORE Step 1, temporarily change `VelopackUpdateService.cs:GitHubRepo`
   to a local HTTP URL like `http://<dev-machine-IP>:8765`.
2. Rebuild alpha.1 with that change applied:
   ```powershell
   pwsh scripts/local-pack.ps1 -Version 1.1.0-alpha.1
   ```
3. Then proceed with Step 2 onward — but skip the GitHub publish step here.
4. On dev machine, serve the Releases folder:
   ```powershell
   python -m http.server 8765 --directory Releases/
   ```
5. Ensure the VM can reach the dev-machine IP on port 8765. Test from VM:
   ```powershell
   Test-NetConnection -ComputerName <dev-machine-IP> -Port 8765
   ```
   Hyper-V Default Switch (NAT) routes VM→host traffic via the host's
   vEthernet adapter at `172.x.x.1` typically. Find the exact IP:
   ```powershell
   # On dev machine:
   Get-NetIPAddress -InterfaceAlias 'vEthernet (Default Switch)' `
     -AddressFamily IPv4 | Select-Object IPAddress
   ```

**Recommended choice for Phase 1 smoke:** Option A. The GitHub Release path
matches production behavior end-to-end; Option B introduces an unnecessary
deviation from production for a Phase 1 ship gate.

### 9. [VM] Trigger update check in the running app

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

### 10. [VM] Verify post-update state

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

After a clean pass, take a Hyper-V snapshot named `post-smoke-1`:

```powershell
# On dev machine (Hyper-V host):
Checkpoint-VM -Name 'WIN11-CONTROL-MENU' -SnapshotName 'post-smoke-1'
```

This is the baseline for Phase 2 (tray, auto-relaunch, Servy integration) testing.

---

## On failure

**On first failure:** STOP. Do not retry immediately.

1. Take a Hyper-V snapshot to preserve VM state:
   ```powershell
   # On dev machine (Hyper-V host):
   $ts = (Get-Date).ToString('yyyyMMdd-HHmmss')
   Checkpoint-VM -Name 'WIN11-CONTROL-MENU' -SnapshotName "smoke-1-fail-$ts"
   ```
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
