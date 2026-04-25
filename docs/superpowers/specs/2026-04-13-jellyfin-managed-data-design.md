# Jellyfin Managed Data — Design Spec

**Date:** 2026-04-13
**Scope:** Auto-configure Jellyfin settings from docker-compose, managed backup/logging directories, operation logging, configurable retention, sqlite3 dependency

---

## 1. Settings

### New Settings (editable in Settings → Jellyfin)

| Setting Key | Description | Default |
|---|---|---|
| `jellyfin-compose-path` | Path to Jellyfin docker-compose.yml | *(required, no default)* |
| `jellyfin-backup-retention-days` | Days to keep database backups | `5` |
| `jellyfin-base-url` | Jellyfin API URL | `http://127.0.0.1:8096` |

### Derived Settings (auto-populated from compose file)

| Setting Key | Derived From | Example |
|---|---|---|
| `jellyfin-db-path` | Compose volume mount ending in `:/config` + `/data/jellyfin.db` | `D:\DockerData\jellyfin\config\data\jellyfin.db` |
| `jellyfin-backup-dir` | App root + `jellyfin-data/backups/` | `C:\...\tools-menu\jellyfin-data\backups\` |
| `jellyfin-container-name` | Compose `container_name:` field, or service name | `jellyfin` |

Derived settings are stored in the DB alongside user settings. They are re-derived whenever the compose path is saved or the user clicks "Refresh from Compose."

### Removed Settings

The following are no longer manually configured — they are derived:
- `jellyfin-db-path` (from compose volume mount)
- `jellyfin-backup-dir` (from app root)
- `jellyfin-container-name` (from compose file)

---

## 2. Docker Compose Parser

A utility method in `JellyfinService` (or a small helper class) that reads a docker-compose.yml and extracts:

1. **Container name** — from `container_name:` property on the Jellyfin service, or the service key name as fallback
2. **Config volume mount** — find the volume entry where the container side ends with `:/config` (or `:/config:rw`, `:/config:ro`). Extract the host side path.
3. **Port mapping** (optional) — if present, can auto-suggest `jellyfin-base-url`

**Parsing approach:** Simple line-by-line text parsing of the YAML. The compose file format is well-structured enough that we don't need a full YAML parser — look for `container_name:`, `volumes:` list entries matching `:/config`, and `ports:` list entries.

**When it runs:**
- When user saves the `jellyfin-compose-path` setting
- When user clicks "Refresh from Compose" button
- Results stored in DB via `SetSettingAsync`

**Error handling:** If the compose file is missing, unreadable, or doesn't contain expected entries, show a clear error in the Settings UI. Don't silently fail.

---

## 3. Managed Directories

```
{app-root}/
  jellyfin-data/
    backups/       ← timestamped .db backup files
    logging/       ← operation log files
```

- Created automatically on first use (when a backup or log write is attempted)
- `jellyfin-data/backups/` is set as `jellyfin-backup-dir` in the DB — not user-configurable (it's always relative to the app root)
- Path derived from `AppContext.BaseDirectory` (same pattern as `dependencies/`)

---

## 4. Operation Logging

### What Gets Logged

Two operation types:
1. **Database Date Update** — the DateCreated = PremiereDate SQL operation
2. **Cast & Crew Update** — the image download operation

### Log Format

One file per operation run:
- Filename: `{operation}_{timestamp}.log` (e.g., `db-date-update_20260413_153045.log`)
- Content: plain text, one line per event

```
2026-04-13 15:30:45 START db-date-update
2026-04-13 15:30:46 STEP  Stopping container jellyfin (8c20fc6b3724)
2026-04-13 15:30:51 OK    Container stopped
2026-04-13 15:30:51 STEP  Creating backup
2026-04-13 15:30:52 OK    Backup saved: jellyfin_20260413_153051.db
2026-04-13 15:30:52 STEP  Running SQL update
2026-04-13 15:30:53 OK    SQL update applied
2026-04-13 15:30:53 STEP  Starting container
2026-04-13 15:30:58 OK    Container restarted
2026-04-13 15:30:58 STEP  Cleaning up old backups
2026-04-13 15:30:58 OK    Removed 2 backups older than 5 days
2026-04-13 15:30:58 DONE  Completed successfully
```

On failure:
```
2026-04-13 15:30:52 FAIL  Backup failed: jellyfin-db-path not configured
```

### UI Presentation

A "Recent Operations" section on the Jellyfin dashboard pages. Shows the last 10 runs with:
- Operation name
- Timestamp
- Status (success/failed)
- Expandable details (shows the log file content)

---

## 5. Backup Retention

### Changes from Current Implementation

- Retention days configurable via `jellyfin-backup-retention-days` setting (default: 5)
- Replace PowerShell/find shell commands with native C# file operations:

```csharp
var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
foreach (var file in Directory.GetFiles(backupDir, "*.db"))
{
    if (File.GetLastWriteTimeUtc(file) < cutoff)
        File.Delete(file);
}
```

- Cross-platform, no external tool dependency
- Runs after every DB update operation (same as current)

---

## 6. sqlite3 Dependency

Add `sqlite3` as a dependency in the Jellyfin module (`JellyfinModule.cs`):

```csharp
new ModuleDependency
{
    Name = "sqlite3",
    ExecutableName = "sqlite3",
    VersionCommand = "sqlite3 --version",
    VersionPattern = @"([\d.]+)",
    SourceType = UpdateSourceType.Manual,
    ProjectHomeUrl = "https://www.sqlite.org/download.html",
    InstallPath = Path.Combine(DepsRoot, "sqlite3")
}
```

The `dependencies/sqlite3/` folder gets prepended to PATH at startup (existing infrastructure from Task 4). Users can manually place `sqlite3.exe` there, or we can add download support later.

---

## 7. Settings UI

The Jellyfin section in Settings gets a dedicated panel:

### Jellyfin Configuration
- **Docker Compose Path** — text input + "Browse" feel (paste path) + "Parse" button
- After parsing, show derived values:
  - Container name: `jellyfin`
  - Database path: `D:\DockerData\jellyfin\config\data\jellyfin.db`
  - Status: checkmark/X for each (file exists? container found?)
- **API Base URL** — text input, default `http://127.0.0.1:8096`
- **Backup Retention** — number input, days, default 5

### Managed Directories (read-only display)
- Backups: `{app-root}/jellyfin-data/backups/` — with file count and total size
- Logs: `{app-root}/jellyfin-data/logging/` — with file count

---

## Out of Scope

- Full YAML parser (simple line parsing is sufficient)
- Jellyfin container log capture (`docker logs`)
- Automatic sqlite3 download (manual placement for now)
- Docker-in-Docker path translation (Phase 8b)
- Backup scheduling (currently only runs as part of DB update)
- Log rotation/retention (log files are small, manual cleanup is fine)
