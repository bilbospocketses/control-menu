# Fresh-VM smoke runbook (ship gate)

**VM:** WIN11-CONTROL-MENU (Hyper-V, baseline snapshot per `user_test_devices.md`).

Validates a tagged Control Menu release end-to-end via the CI-built MSI:
Velopack-installed ControlMenu boots, persists user data outside `current\`,
and (optionally) the in-app update flow completes between `{old}` and `{new}`.

The primary flow uses GitHub Actions to build the MSI and publish a GitHub
Release; the VM downloads the MSI directly. The local-pack flow remains
documented at the bottom as a dev-iteration alternate.

## Two flow modes — pick one or both

- **Mode A — Install smoke** (steps 0-6). Validates a single MSI on a fresh VM.
  Use for first cuts of a new major/minor (e.g. `v1.1.0` stable) where there is
  no in-app-update predecessor worth chaining against, or for any smoke where
  you only need to validate the install + first-run + persistence shape.
- **Mode B — Install + in-app update smoke** (steps 0-9). All of Mode A, then
  chains to a second tag `{new}` to validate the Velopack update flow from
  `{old}` → `{new}`. Requires both versions installable on a clean host AND
  `{old}`'s Settings → General page loads cleanly (the in-app "Check for
  updates" button lives on that page).

### Version-chain prerequisites for Mode B

Settings → General must load on `{old}` — that page hosts the
"Check for updates" button. If `{old}` crashes on Settings → General, the
in-app update flow is unreachable and you must fall back to Mode A on
`{new}` (fresh install over `{old}` rather than via in-app upgrade).

> *Historical context:* During the Phase 1 chain, `v1.1.0-beta.2` crashed
> Settings → General due to a per-process `VelopackLocator` init bug.
> `v1.1.0-beta.3` shipped the fix, but the in-app update from beta.2 → beta.3
> was never validated end-to-end — beta.3 was installed FRESH over beta.2.
> The in-app update flow first becomes validatable when two consecutive
> working installable releases exist on the channel.

## Machine ownership at a glance

| Step | Machine | Action | Modes |
|---|---|---|---|
| 0 | [DEV BOX] | Verify CI release pipeline is set up; one-time | A + B |
| 1 | [DEV BOX] | Tag `{old}`; push; wait for Actions to publish GitHub Release | A + B |
| 2 | [VM] | Download `{old}` MSI from GitHub Release | A + B |
| 3 | [VM] | Run MSI; UAC; PerMachine install | A + B |
| 4 | [VM] | Verify on-disk layout | A + B |
| 5 | [VM] | Launch; click through pages; verify data root created | A + B |
| 6 | [VM] | Persist user data (camera + Jellyfin) | A + B |
| 7 | [DEV BOX] | Tag `{new}`; push; wait for Actions | B only |
| 8 | [VM] | Trigger update check in running app | B only |
| 9 | [VM] | Verify post-update state | B only |
| Post-smoke | [DEV BOX] | Hyper-V checkpoint | A + B |

(Mode A: stop after step 6 and skip directly to the Mode-A pass criteria below.
Mode B: run all 9 steps.)

---

## Pre-flight (one-time VM setup, if baseline snapshot doesn't already have it)

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
5. Memorize the version strings under test:
   - Mode A: `{old}` (single version under test).
   - Mode B: `{old}` and `{new}` (chain endpoints).

If the baseline already has a non-admin `cm-test` (or similar) user, skip
steps 2-4.

---

## Steps

### 0. [DEV BOX] (One-time) Verify GitHub Actions release pipeline

Before the first tag push of a given chain, sanity-check:

1. **Workflow file present:** confirm `.github/workflows/release.yml` exists on
   the branch you're about to tag.
2. **Repo permissions:** GitHub repo Settings -> Actions -> General ->
   Workflow permissions = "Read and write permissions" (needed for the `publish`
   job to create a Release). Default for personal repos.
3. **SIGNING_API_TOKEN secret:** Phase 1 + early Phase 4 ship unsigned (no
   secret set → `signing_mode == 'unsigned'`, signing steps no-op). Phase 4
   wires Azure Trusted Signing via `azure/artifact-signing-action`; that step
   activates when the secret is set.
4. **Transitive SHA pinning baseline:** the in-use composite actions
   (`actions/attest-build-provenance` and its transitive `actions/attest`)
   are SHA-pinned upstream as of v4.1.0 + v4.1.0 respectively. If Phase 4
   adds `azure/artifact-signing-action` to the workflow, pin it to commit SHA
   (not tag-object SHA) and add the matching `patterns_allowed` allowlist
   entry — see release.yml's FUTURE SIGNER comment block for the full pin
   recipe.

### 1. [DEV BOX] Tag `{old}` and wait for the Release

```powershell
# All git operations use -C to scope to the repo absolute path
# (per multi-session cwd discipline — never `cd` into the repo).
git -C C:/Users/jscha/source/repos/control-menu tag {old}
git -C C:/Users/jscha/source/repos/control-menu push origin {old}
```

(Substitute `{old}` with the actual version string, e.g. `v1.1.0` or `v1.1.0-beta.4`.)

**Watch the Actions run:**
- Open `https://github.com/bilbospocketses/control-menu/actions` in a browser.
- The `Release` workflow should appear, triggered by the tag push.
- Wait for completion (~5-10 minutes for the build-windows + publish jobs).

**Verify the GitHub Release was created:**
- Navigate to `https://github.com/bilbospocketses/control-menu/releases`.
- The `{old}` Release should be present with these assets (substitute the
  numeric portion of `{old}` into the filenames — e.g. `v1.1.0` → `1.1.0`):
  - `ControlMenu-<old-numeric>-win-Setup.msi`
  - `ControlMenu-<old-numeric>-win.nupkg` (full)
  - `ControlMenu-<old-numeric>-win-delta.nupkg` (delta; may be absent for the
    first release on a channel)
  - `releases.<channel>.json`
  - `SHA256SUMS`

**If the Actions run fails:** view the workflow logs in the Actions tab. Common
failure modes:
- vpk install version conflict (rare; rerun usually resolves).
- MSI signing step (should be skipped in unsigned mode -- if it fires, the
  conditional is broken).
- Action-resolution failure on a composite `uses:` chain that drifted off
  SHA-pin (transitive SHA pinning enforcement; see
  `feedback_transitive_sha_pinning.md`).

Surface to user.

### 2. [VM] Download `{old}` MSI from the GitHub Release

Restore the VM to baseline and sign in as `cm-test`:

```powershell
# On dev machine to restore + start VM:
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
- Settings -> General (**Mode B prerequisite gate** — this page must render
  cleanly or in-app update is unreachable; see Version-chain prerequisites)
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

**Mode A users: STOP HERE.** Skip directly to the Mode-A pass criteria below.

### 7. [DEV BOX] Tag `{new}` with a trivial change (Mode B only)

1. Bump CHANGELOG.md: add a `[<new-numeric>]` section.
2. Commit the bump (PR-gated repo — branch + PR + CI green + squash-merge):
   ```powershell
   git -C C:/Users/jscha/source/repos/control-menu checkout -b chore/release-{new} master
   # (edit CHANGELOG.md to promote [Unreleased] → [<new-numeric>])
   git -C C:/Users/jscha/source/repos/control-menu add CHANGELOG.md
   git -C C:/Users/jscha/source/repos/control-menu commit -m "chore(release): bump to <new-numeric>"
   git -C C:/Users/jscha/source/repos/control-menu push -u origin chore/release-{new}
   # Open PR, wait for 5-check gate (build-and-test + 3 CodeQL Analyses +
   # Scorecard analysis) to go green, squash-merge.
   ```
3. Tag + push (from updated master):
   ```powershell
   git -C C:/Users/jscha/source/repos/control-menu checkout master
   git -C C:/Users/jscha/source/repos/control-menu pull origin master
   git -C C:/Users/jscha/source/repos/control-menu tag {new}
   git -C C:/Users/jscha/source/repos/control-menu push origin {new}
   ```
4. Wait for the Actions run to complete and the GitHub Release to publish.

The `VelopackUpdateService` points at the CM GitHub repo via GithubSource.
Once the GitHub Release for `{new}` exists, the running `{old}` app
can discover it.

**CRITICAL: do NOT set `--prerelease`** (the CI workflow already omits it).
Velopack's GithubSource queries `/releases/latest` which excludes prereleases
(Gotcha 5 in `feedback_velopack_permachine_lessons.md`). Channel separation is
handled by the per-channel `releases.<channel>.json` feed file; the prerelease
flag would break discovery without adding value.

**VM network requirement:** The VM must have internet access to reach
`github.com` and download the release assets. Hyper-V Default Switch (NAT)
provides this by default.

### 8. [VM] Trigger update check in the running app (Mode B only)

In the browser at `http://localhost:5159`:
1. Navigate to Settings -> General -> Updates.
2. Click "Check for updates".
3. An update-available banner shows `<new-numeric>`.
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

### 9. [VM] Verify post-update state (Mode B only)

After manual relaunch:

**Version check:**
- Open Settings -> General. The displayed version should be `<new-numeric>`.
- Or check file mtime: `C:\Program Files\ControlMenu\current\ControlMenuLauncher.exe`
  mtime should be after the `{new}` build time.

**User data survived:**
- Settings -> Cameras: the fake camera from step 6 is still there.
- Settings -> Jellyfin: the Compose path from step 6 is still there.
- `C:\ProgramData\ControlMenu\config\controlmenu.db` mtime predates the
  `{new}` install (the DB was NOT replaced by the update).

---

## Pass criteria

### Mode A (install only)

Steps 0-6 complete without crashes or ERROR log entries.

Specifically:
- `launcher.log` and `controlmenu.log` contain only INFO-level entries for
  the full install + first-run sequence.
- All click-through pages from step 5 render without circuit teardown.
- `C:\ProgramData\ControlMenu\` was created on first launch with the expected
  subdirectories (config, logs, dependencies).
- The persisted camera + Jellyfin Compose path from step 6 are saved
  (verifiable on app restart).

### Mode B (install + in-app update)

All Mode A criteria PLUS steps 7-9 complete without crashes or ERROR log
entries.

Specifically:
- `<new-numeric>` binaries are present in `current\` after manual relaunch.
- Camera entry and Jellyfin Compose path from step 6 are unchanged after
  the update.
- `C:\ProgramData\ControlMenu\` was NOT touched by the Velopack update
  (user data lives outside `current\` by design -- the core Phase 1 invariant).

---

## Post-smoke snapshot

After a clean pass, take a Hyper-V snapshot named `post-smoke-<tag>`
(substitute the tag, e.g. `post-smoke-v1.1.0`):

```powershell
# On dev machine (Hyper-V host):
Checkpoint-VM -Name 'WIN11-CONTROL-MENU' -SnapshotName 'post-smoke-{old}'
```

This is the baseline for the next phase's testing.

---

## On failure

**On first failure:** STOP. Do not retry immediately.

1. Take a Hyper-V snapshot to preserve VM state:
   ```powershell
   # On dev machine (Hyper-V host):
   $ts = (Get-Date).ToString('yyyyMMdd-HHmmss')
   Checkpoint-VM -Name 'WIN11-CONTROL-MENU' -SnapshotName "smoke-fail-$ts"
   ```
2. Copy logs off the VM:
   ```
   C:\ProgramData\ControlMenu\logs\launcher.log
   C:\ProgramData\ControlMenu\logs\controlmenu.log
   ```
   Save to `<repo>/scratch/smoke-fail-<timestamp>/` on the dev machine.
3. Roll the VM back to the baseline snapshot.
4. Diagnose on dev. Fix on a feature branch off master (PR-gated repo —
   no direct push). Re-run from step 1 once the fix lands.

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
pwsh C:/Users/jscha/source/repos/control-menu/scripts/local-pack.ps1 -Version 1.1.0-local
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
