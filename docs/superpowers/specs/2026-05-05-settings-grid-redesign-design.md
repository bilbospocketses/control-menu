# Settings Grid Redesign — Design Spec

**Date:** 2026-05-05
**Branch:** `feature/settings-grid-redesign`
**Origin TODO:** `todo_control_menu.md` item 2 (General Settings page refinement, UX overhaul cluster)

## Goal

Replace the flat vertical-stack layout on the General Settings and Jellyfin Settings pages with a 2-column grid-of-cells inside each section, plus reordering, scope-cleanup, and a few new configurable settings on Jellyfin.

The current layout renders settings as a flat list of `form-group` divs. There is no visual rhythm, no dense use of horizontal space, and on wide monitors most settings end up far apart with empty area on the right. The redesign adopts a sectioned 2-column grid where the section title bar spans the top, and individual settings fill cells below.

The grid pattern is templated as three small reusable Razor components so the same look-and-feel can be applied to future settings pages without copying CSS or markup.

## Scope

In scope:

- New components: `SettingsSection`, `SettingsGrid`, `SettingsGridCell`.
- Rewrite of `GeneralSettings.razor` against the new components.
- Rewrite of `JellyfinSettingsSection.razor` against the new components, including:
  - Removal of the Backup Directory row from the Docker Compose parse-result table.
  - New combined section "Logging, Backup & Retention" replacing the separate "Backup & Retention" + "Managed Directories" sections.
  - Backup directory and log directory become user-configurable settings.
  - Editing the Backups path migrates existing `*.db` files; editing the Logs path migrates existing `*.log` files.
- Sub-nav reorder in `SettingsPage.razor`: Jellyfin moves to position 2.
- Save-button pattern: Jellyfin API and Cast & Crew Notifications switch from per-field auto-save to a single per-section Save button. Diskette icon (`bi-floppy`) on Jellyfin Save buttons except the existing "Save & Parse" which keeps `bi-arrow-repeat`.
- bUnit tests for the new components.
- Manual-test-checklist additions for the new behaviors.

Out of scope:

- Cameras, Android Devices, and Dependencies settings pages — those are well-suited to their purpose and stay untouched.
- General page's SMTP block stays auto-save (no explicit Save button).
- Jellyfin file migration UI beyond the success/partial/error notification.
- Theme/Wizard/Test-Email button styling on General — those are action buttons, not save buttons.

## Component architecture

Three components live in `Components/Shared/Settings/`. Each has a scoped CSS file (`*.razor.css`) so styles are self-contained — required by the project's Blazor scoped-CSS conventions.

### `SettingsSection.razor`

The boxed card with title bar. Single `Title` parameter; renders title bar + free-form `ChildContent` below.

```razor
<SettingsSection Title="General">
    <SettingsGrid>
        ...
    </SettingsGrid>
</SettingsSection>
```

CSS: outer border, rounded corners, `var(--surface-2)` background; title bar at `var(--surface-3)` with bottom divider, uppercase tracking, weight 600.

### `SettingsGrid.razor`

A 2-column CSS grid that lays out children. No parameters; just renders `<div class="settings-grid">@ChildContent</div>`. The children are expected to be `SettingsGridCell` components, but the grid does not enforce this — any block children will be laid out in 2 columns.

CSS: `display: grid; grid-template-columns: 1fr 1fr;` with internal cell dividers via per-cell border-top + border-right (no border on the outer edges or first row's top edge).

### `SettingsGridCell.razor`

A single grid cell with three optional named slots and a `FullRow` parameter.

```razor
<SettingsGridCell>
    <Label>Theme</Label>
    <ChildContent>
        <button>...</button>
    </ChildContent>
    <Hint>Also available from the icon in the top-right of every page.</Hint>
</SettingsGridCell>

<SettingsGridCell FullRow="true">
    <ChildContent>
        ... wide content like the Compose path input + Save & Parse button ...
    </ChildContent>
</SettingsGridCell>
```

- `Label` (RenderFragment, optional): rendered as uppercase 12px small caps with `var(--muted)` color and 4px bottom margin. Cell with no Label has no label header.
- `ChildContent` (RenderFragment): the cell body — control, button, table, anything.
- `Hint` (RenderFragment, optional): rendered below ChildContent as 11px `var(--muted)` text.
- `FullRow` (bool, default false): when true, the cell uses `grid-column: 1 / -1` to span the full row.

A "Save" cell (e.g., the Jellyfin API section's bottom-right Save button) is just a `SettingsGridCell` with no Label, no Hint, just a button as ChildContent — flexbox-aligned to bottom-right via cell-internal styles.

## GeneralSettings.razor rewrite

Three `SettingsSection`s, all with one `SettingsGrid` inside (no free-form content beyond the grid).

### Section "General"

Reorder: Setup Wizard, Timezone, Theme (was: Theme, Timezone, Setup Wizard).

| Cell 1 | Cell 2 |
|---|---|
| Setup Wizard — `[↻ Re-run Setup Wizard]`. Hint: "Walk through the initial setup again." | Timezone — current dropdown. Hint: "Timezone used for log timestamps." |
| Theme — `[🌙 Dark] [☀ Light]` toggle pair. Hint: "Also available from the icon in the top-right of every page." | *(empty — `SettingsGridCell` with no Label / ChildContent renders empty cell)* |

Behavior unchanged: theme buttons call `SetTheme`, timezone @onchange calls `SaveTimezone`, wizard button calls `RerunWizard`. No save button — these are immediate-effect or auto-save patterns.

### Section "Email (SMTP)"

| Cell 1 | Cell 2 |
|---|---|
| SMTP Server | SMTP Port |
| Username | Password |
| From Email + hint "Sender address for outgoing emails. Must be authorized by your SMTP provider." | Notification Email + hint "Default recipient for all notification emails." |
| `[✉ Send Test Email]` button | *(empty)* |

The intro paragraph "Configure SMTP for sending notification emails from any module." is dropped — labels carry the meaning.

Behavior unchanged: each field auto-saves on `@onchange` calling `SaveSmtpServer` etc. Test email button stays as-is.

### Section "ws-scrcpy-web deployment"

Single grid row, two cells:

| Cell 1 | Cell 2 |
|---|---|
| Deployment Mode — radios with inline descriptions:<br>• Managed — Control Menu spawns and watches the node process on port 8000.<br>• External — Connect to a running ws-scrcpy-web at the External URL. | External URL — input. Disabled when `_wsscrcpyMode == "managed"` (always rendered, just toggles `disabled` attribute and reduced opacity). Hint: "Disabled until External mode is selected." |

Behavior change: the URL cell always renders. The `@if (_wsscrcpyMode == "external")` block goes away. `SaveUrl` is still wired to `@onblur` of the input.

### Notification message

The bottom-of-page success/error alert (`<div class="alert ...">`) stays as-is.

## JellyfinSettingsSection.razor rewrite

Four `SettingsSection`s, in this order:

### Section "Docker Compose"

Intro paragraph stays: "Point Control Menu to your Jellyfin docker-compose.yml to auto-detect container name and database path."

One `FullRow` `SettingsGridCell`:

- Label: "Compose File Path"
- ChildContent: input (`@bind="_composePath"`) + `[↻ Save & Parse]` button on the same line via flexbox.
- Below the cell content (still within the `FullRow` cell), the conditional `_parseResult` rendering:
  - On error: existing `alert alert-danger`.
  - On success: `data-table` with two rows — Container Name + Database Path. **Backup Directory row removed.**

The Save & Parse button keeps `bi-arrow-repeat` — compound action; iconography intentional.

### Section "Jellyfin API"

| Cell 1 | Cell 2 |
|---|---|
| Base URL | API Key (password field) |
| User ID + hint "Jellyfin user ID for API calls (used by Cast & Crew updates)." | `[💾 Save Jellyfin API]` button (no Label, no Hint, flex-aligned bottom-right) |

Behavior change: per-field `@onchange` handlers (`SaveBaseUrl`, `SaveApiKey`, `SaveUserId`) are removed. Inputs use `@bind` to local state. Single Save button calls a new `SaveJellyfinApi` handler that:

1. Writes `jellyfin-base-url` setting.
2. Writes (or clears) `jellyfin-api-key` secret depending on whether the field is non-empty.
3. Writes `jellyfin-user-id` setting.
4. Shows "Jellyfin API saved." notification.

Always-enabled — no dirty tracking, clicking saves all fields regardless of changes. Matches the existing pattern for sections like Backup & Retention's current Save button.

### Section "Cast & Crew Notifications"

| Cell 1 | Cell 2 |
|---|---|
| Notification Email + hint "Receives completion alerts for Cast & Crew updates. Leave blank to use the default notification email from General settings." | `[💾 Save Notification Email]` button |

Behavior change: `SaveCastCrewEmail` becomes button-triggered; field uses `@bind` to local state.

### Section "Logging, Backup & Retention"

No `SettingsGrid`. Body is a `data-table` with header row "Setting | Value | (action)":

| Setting | Value | |
|---|---|---|
| **Backups** | path input + small `<span>` showing current file count + total size | `[💾 Save]` |
| **Logs** | path input + small `<span>` showing current file count | `[💾 Save]` |
| **Retention** | number input + "days" suffix | `[💾 Save]` |

Stats are populated from the existing `RefreshDirectoryStats()` against the current saved path. After a successful path Save, refresh stats and re-render.

#### New configuration

Two new settings keys:

- `jellyfin-backup-directory` (string, optional). Empty/null = fall back to `OperationLogger.GetBackupDirectory()` derived default.
- `jellyfin-log-directory` (string, optional). Empty/null = fall back to `OperationLogger.GetLogDirectory()` derived default.

`OperationLogger` (or a thin resolver) consults `IConfigurationService` for these overrides when computing the effective path. Implementation can either:

- Inject `IConfigurationService` into `OperationLogger` and resolve on each call (simple, but synchronous-async friction since current callers are sync).
- Cache the effective path in a singleton service that listens for config changes (more correct, more code).

Implementation will go with the first approach unless friction surfaces during the implementation plan — `OperationLogger` already accesses configuration via static helpers in some paths, and per-call resolution is cheap.

#### File migration on path change

When the user enters a new Backups path and clicks Save:

1. Validate new path: if it does not exist, attempt `Directory.CreateDirectory`. If it cannot be created or is not writable, surface error and abort (do not save the setting).
2. Enumerate `*.db` files in the old (currently effective) backups directory.
3. For each file, attempt `File.Move(src, dst)`. On per-file failure (e.g., `IOException` from a file lock), log the failure and continue — do not abort the batch.
4. Save the `jellyfin-backup-directory` setting to the new path (regardless of partial migration outcome — the new path becomes canonical).
5. Refresh stats against the new path.
6. Surface the outcome via the standard notification:
   - Full success: "Backups path saved. Moved {n} files to {newPath}."
   - Partial: "Backups path saved. Moved {n} of {total} files; {failedFiles} could not be moved (in use). Retry the Save to migrate them."
   - No files to move: "Backups path saved."
   - Validation failure: "Could not save: {reason}" (setting not changed).

Logs path follows the same flow with `*.log` files. Note: log files actively being written by the running app are likely to be locked on Windows; the partial-move path handles this gracefully and the user can either restart Control Menu or wait for log rotation before retrying.

Old directory is left in place after migration — never auto-deleted. User can clean up manually.

#### Retention save

Retention save is unchanged behavior; the row just visually moves into the new combined table. Setting key stays `jellyfin-backup-retention-days`.

### Notification message

Bottom-of-page success/error alert stays.

## SettingsPage.razor sub-nav reorder

Lines 8–27 of `SettingsPage.razor`. New button order:

1. General
2. Jellyfin
3. Android Devices
4. Cameras
5. Dependencies

Three lines move; no route changes (`/settings/jellyfin` still resolves).

## Save-button iconography

Bootstrap Icons `bi-floppy` (or `bi-floppy-fill` if available — implementation should pick the cleaner of the two when wiring up):

- Jellyfin API → Save Jellyfin API
- Cast & Crew → Save Notification Email
- Logging/Backup/Retention table → 3× Save (one per row)

`bi-arrow-repeat` retained:

- Docker Compose → Save & Parse (compound action)

`bi-envelope`, `bi-moon-fill`, `bi-sun-fill`, `bi-arrow-counterclockwise` retained on their respective non-save buttons.

## Tests

bUnit tests for the new components in `tests/ControlMenu.Tests/Components/Shared/Settings/`:

- `SettingsSection_RendersTitleAndChildContent`
- `SettingsGrid_LaysOutChildrenInTwoColumns`
- `SettingsGridCell_RendersAllSlots`
- `SettingsGridCell_OmitsLabelHeaderWhenLabelSlotEmpty`
- `SettingsGridCell_FullRowAppliesGridColumnSpan`

Service-layer tests for the migration logic in the appropriate test file (likely a new `JellyfinDirectoryMigrationTests.cs`):

- `MoveBackupFiles_HappyPath_AllFilesMoved`
- `MoveBackupFiles_OneFileLocked_ReturnsPartialResult`
- `MoveBackupFiles_TargetDirDoesNotExist_CreatesIt`
- `MoveBackupFiles_TargetDirNotWritable_ReturnsValidationError`
- Same shape for log files.

No bUnit tests for `GeneralSettings.razor` or `JellyfinSettingsSection.razor` directly — those bind heavily to `IConfigurationService` and JS interop, and the manual checklist remains the source of truth for whole-page behavior.

## Manual test checklist additions

`docs/manual-test-checklist.md` Settings section gets new items:

- Settings sub-nav: order is General, Jellyfin, Android Devices, Cameras, Dependencies.
- General page: Theme/Timezone/Setup Wizard render in the new General section in correct order; Theme cell shows the "also available top-right" hint.
- General page: SMTP fields render in 2-column grid; Test Email button still works.
- General page: ws-scrcpy-web URL field is disabled in Managed mode, enabled in External mode.
- Jellyfin page: Docker Compose parse-result table shows Container + DB only (no Backup Directory).
- Jellyfin page: Jellyfin API Save button persists all three fields.
- Jellyfin page: Cast & Crew Save button persists email.
- Jellyfin page: Logging/Backup table — change Backups path to a new directory; existing `.db` files migrate; stats reflect new location.
- Jellyfin page: Logs path migration — at least one log file likely locked; partial-success notification surfaces; retry-after-restart succeeds.
- Jellyfin page: Retention save persists value.

## Branch and commits

Branch: `feature/settings-grid-redesign`. Per the project's branch-by-default rule.

Commit shape (suggested grouping for the implementation plan):

1. Components scaffold + scoped CSS (`SettingsSection`, `SettingsGrid`, `SettingsGridCell` + tests).
2. `GeneralSettings.razor` rewrite.
3. `JellyfinSettingsSection.razor` rewrite — non-migration sections (Docker Compose, Jellyfin API, Cast & Crew).
4. New `jellyfin-backup-directory` / `jellyfin-log-directory` settings + `OperationLogger` resolution + migration logic + service tests.
5. Logging/Backup/Retention table on Jellyfin page wired to migration + Save handlers.
6. `SettingsPage.razor` sub-nav reorder.
7. Manual checklist additions + CHANGELOG `[Unreleased]` entries.

CHANGELOG entries land under `[Unreleased]` Added / Changed / Removed as appropriate (Removed: "Backup Directory row from Docker Compose parse-result table"; Changed: "Settings sub-nav reordered, Jellyfin now position 2"; Added: "Configurable Backups and Logs directories with file migration on path change", etc.).

## Open assumptions to flag at implementation time

- **`OperationLogger` integration.** Spec assumes per-call resolution via `IConfigurationService`. If `OperationLogger` is heavily static and DI-injection requires more refactoring than expected, the implementation plan will surface that and propose a narrower fix.
- **bi-floppy availability.** If the project's Bootstrap Icons font does not include `bi-floppy` / `bi-floppy-fill`, fall back to the closest alternative (`bi-save`, `bi-hdd`) and document the choice in the implementation commit.
- **Migration "retry the Save"** wording assumes the user will rerun Save. If iteration shows that's confusing, a dedicated "Retry migration" button can be added in a follow-up — not in this spec.
