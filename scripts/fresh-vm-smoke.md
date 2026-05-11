# Fresh-VM smoke #1 runbook (Phase 1 ship gate)

**VM:** WIN11-CONTROL-MENU (Hyper-V, baseline snapshot per `user_test_devices.md`).

This runbook verifies the Phase 1 deliverable end-to-end via the CI-built MSI:
Velopack-installed ControlMenu boots, persists user data outside `current\`,
and the manual update flow completes (beta.1 -> beta.2). No tray, no Servy,
no signing yet -- those are Phase 2-4.

The primary flow uses GitHub Actions to build the MSI and publish a GitHub
Release; the VM downloads the MSI directly. The local-pack flow remains
documented at the bottom as a dev-iteration alternate.

**Machine ownership at a glance:**

| Step | Machine | Action |
|---|---|---|
| 0 | [DEV BOX] | Verify CI is set up; one-time |
| 1 | [DEV BOX] | Tag beta.1; push; wait for Actions to publish GitHub Release |
| 2 | [VM] | Download beta.1 MSI from GitHub Release |
| 3 | [VM] | Run MSI; UAC; PerMachine install |
| 4 | [VM] | Verify on-disk layout |
| 5 | [VM] | Launch; click through pages; verify data root created |
| 6 | [VM] | Persist user data (camera + Jellyfin) |
| 7 | [DEV BOX] | Tag beta.2; push; wait for Actions |
| 8 | [VM] | Trigger update check in running app |
| 9 | [VM] | Verify post-update state |
| Post-smoke | [DEV BOX] | Hyper-V checkpoint |

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
   - Settings -> Accounts -> Other users -> Add account -> "I don't have this
     person's sign-in information" -> "Add a user without a Microsoft account".
   - Username: `cm-test`, password: anything memorable.
   - DEFAULT account type is Standard (non-admin) -- that's what we want.
     Do NOT promote to admin.
3. Sign out of the admin account, sign into `cm-test`.
4. Confirm `cm-test` is non-admin: open Settings -> Accounts -> Your info --
   should NOT say "Administrator" below the username.
5. Have two version strings memorized: `1.1.0-beta.1` and `1.1.0-beta.2`.

If the baseline already has a non-admin `cm-test` (or similar) user, skip
steps 2-4.

---

## Steps

### 0. [DEV BOX] (One-time) Verify GitHub Actions release pipeline

Before the first tag push, sanity-check:

1. **Workflow file present:** confirm `.github/workflows/release.yml` exists on
   the branch you're about to tag.
2. **Repo permissions:** GitHub repo Settings -> Actions -> General ->
   Workflow permissions = "Read and write permissions" (needed for the `publish`
   job to create a Release). Default for personal repos.
3. **No SIGNING_API_TOKEN secret yet:** Phase 1 is unsigned. The signing_mode
   output will be `unsigned`; signing steps no-op. Phase 4 will set up Azure
   Trusted Signing.

### 1. [DEV BOX] Tag beta.1 and wait for the Release

```powershell
cd C:/Users/jscha/source/repos/control-menu

# Ensure feature/velopack-phase-1 has been merged to master FIRST.
# (Or tag the feature branch directly -- Actions runs on any tag push.)
git tag v1.1.0-beta.1
git push origin v1.1.0-beta.1
```

**Watch the Actions run:**
- Open `https://github.com/bilbospocketses/control-menu/actions` in a browser.
- The `Release` workflow should appear, triggered by the tag push.
- Wait for completion (~5-10 minutes for the build-windows + publish jobs).

**Verify the GitHub Release was created:**
- Navigate to `https://github.com/bilbospocketses/control-menu/releases`.
- The `v1.1.0-beta.1` Release should be present with these assets:
  - `ControlMenu-1.1.0-beta.1-win-Setup.msi`
  - `ControlMenu-1.1.0-beta.1-win.nupkg` (full)
  - `ControlMenu-1.1.0-beta.1-win-delta.nupkg` (delta, may be absent for the first release)
  - `releases.beta.json`
  - `SHA256SUMS`

**If the Actions run fails:** view the workflow logs in the Actions tab. Common
Phase 1 failures: vpk install version conflict (rare; rerun usually resolves),
MSI signing step (should be skipped in unsigned mode -- if it fires, the
conditional is broken). Surface to user.

### 2. [VM] Download beta.1 MSI from the GitHub Release

Restore the VM to baseline and sign in as `cm-test`:

```powershell
# Run on dev machine to restore + start VM:
Restore-VMSnapshot -VMName 'WIN11-CONTROL-MENU' -Name 'baseline' -Confirm:$false
Start-VM -VMName 'WIN11-CONTROL-MENU'
```

Inside the VM, sign in as `cm-test` (non-admin). Open Edge and navigate to:
`https://github.com/bilbospocketses/control-menu/releases/latest`

Click the `.msi` asset to download. Save to `Downloads\`.

(VM network requirement: the baseline snapshot must have internet access to
reach github.com. Hyper-V Default Switch with NAT routing provides this by
default.)

### 3. [VM] Run Setup.msi on VM as non-admin user

- SmartScreen warning expected (unsigned CI build) -- "More info" -> "Run anyway".
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

Note the exact values -- you will verify them again after the update in step 9.

### 7. [DEV BOX] Tag beta.2 with a trivial change

1. Bump CHANGELOG.md: add a `[1.1.0-beta.2]` section with "Smoke test bump".
2. Commit:
   ```powershell
   git commit -m "chore: bump to 1.1.0-beta.2 for smoke"
   ```
3. Tag + push:
   ```powershell
   git tag v1.1.0-beta.2
   git push origin v1.1.0-beta.2
   ```
4. Wait for the Actions run to complete and the GitHub Release to publish.

The `VelopackUpdateService` points at the CM GitHub repo via GithubSource.
Once the GitHub Release for v1.1.0-beta.2 exists, the running beta.1 app
can discover it.

**CRITICAL: do NOT set `--prerelease`** (the CI workflow already omits it).
Velopack's GithubSource queries `/releases/latest` which excludes prereleases
(Gotcha 5 in `feedback_velopack_permachine_lessons.md`). Channel separation is
handled by the per-channel `releases.<channel>.json` feed file; the prerelease
flag would break discovery without adding value.

**VM network requirement:** The VM must have internet access to reach
`github.com` and download the release assets. Hyper-V Default Switch (NAT)
provides this by default.

### 8. [VM] Trigger update check in the running app

In the browser at `http://localhost:5159`:
1. Navigate to Settings -> General -> Updates.
2. Click "Check for updates".
3. An update-available banner shows `1.1.0-beta.2`.
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

### 9. [VM] Verify post-update state

After manual relaunch:

**Version check:**
- Open Settings -> General. The displayed version should be `1.1.0-beta.2`.
- Or check file mtime: `C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe`
  mtime should be after the beta.2 build time.

**User data survived:**
- Settings -> Cameras: the fake camera from step 6 is still there.
- Settings -> Jellyfin: the Compose path from step 6 is still there.
- `C:\ProgramData\ControlMenu\config\controlmenu.db` mtime predates the
  beta.2 install (the DB was NOT replaced by the update).

---

## Pass criteria

All 9 steps complete without crashes or ERROR log entries.

Specifically:
- `launcher.log` and `controlmenu.log` contain only INFO-level entries for
  the full install + first-run + update-check + download + apply + relaunch
  sequence.
- `1.1.0-beta.2` binaries are present in `current\` after manual relaunch.
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

---

## Appendix -- local-pack alternate (dev iteration without tags)

The CI-driven flow above is the primary smoke path. If you want to iterate
locally without burning git tags (e.g., debugging a packaging issue), the
`scripts/local-pack.ps1` script produces an equivalent MSI locally on your
dev machine:

```powershell
pwsh scripts/local-pack.ps1 -Version 1.1.0-beta.local
```

The output is in `Releases/`. Transfer to the VM via Hyper-V Enhanced Session
drive redirection (Hyper-V Manager -> Connect -> Show Options -> Local
Resources -> More... -> check your drive) or an SMB share on the dev machine:

```powershell
New-SmbShare -Name "ControlMenuReleases" `
  -Path "C:\Users\jscha\source\repos\control-menu\Releases" `
  -ReadAccess "Everyone"
```

On VM: open File Explorer, type `\\<DEV-MACHINE-IP>\ControlMenuReleases` in
the address bar. Find the dev machine's Hyper-V switch IP:

```powershell
# On dev machine:
Get-NetIPAddress -InterfaceAlias 'vEthernet (Default Switch)' `
  -AddressFamily IPv4 | Select-Object IPAddress
```

For Default Switch (NAT), the host typically appears to the VM as `172.x.x.1`.

The local-pack alternate uses the SAME `vpk pack` flags as CI -- output is
functionally equivalent. The CI flow exists to give you a publish-and-go
workflow without copy-paste overhead.

The `local-pack.ps1` script auto-vendors .NET 10 SDK to
`scripts/dependencies/dotnet/<version>/` on first run (requires internet
access + ~700MB free disk). Subsequent runs are offline-capable.
