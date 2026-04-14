# Control Menu UI Overhaul — Design Spec

**Date:** 2026-04-13
**Scope:** Dark mode palette, theme toggle, sidebar nav styling, dependency update buttons

---

## 1. Dark Mode

### Theme Toggle

Replace the 3-state cycle (system → dark → light → system) with a simple 2-state toggle:
- **Dark ↔ Light** only, no "system" option
- Default to **dark** on first visit
- Stored in `localStorage` key `controlmenu-theme`
- Toggle button in TopBar shows sun icon (when dark) / moon icon (when light)

### Dark Palette

Replace the current navy/blue-tinted dark theme with the OAO grey palette. Zero blue in the dark theme — all neutral greys with emerald green as the only accent color.

```css
[data-theme="dark"] {
    --bg-primary: #2a2a2a;
    --content-bg: #333333;
    --sidebar-bg: #252525;
    --topbar-bg: #252525;
    --border-color: #444444;
    --text-primary: #e0e0e0;
    --text-secondary: #aaaaaa;
    --text-muted: #888888;
    --hover-bg: rgba(255, 255, 255, 0.05);
    --active-bg: rgba(16, 185, 129, 0.1);
    --accent-color: #10b981;
    --card-bg: #333333;
    --card-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
    --input-bg: #2e2e2e;
    --input-border: #444444;
    --danger-color: #f06c75;
    --success-color: #50c878;
    --warning-color: #ffd866;
}
```

### Light Palette

No changes — keep existing Bootstrap-inspired whites/greys.

### Files

- `wwwroot/css/theme.css` — replace dark mode custom properties
- `wwwroot/js/theme.js` — simplify to 2-state toggle, remove system detection
- `Components/Layout/TopBar.razor` — simplify toggle button (2-state, sun/moon icons)

---

## 2. Sidebar Navigation

### Style: Pill Buttons

Replace the current blue underlined links with rounded pill-style buttons.

### Active State
- `background: var(--content-bg)` (#333333 dark / #f8f9fa light)
- `border-radius: 6px`
- `color: var(--text-primary)`
- Icon opacity: 1.0

### Hover State
- `background: var(--hover-bg)`
- `border-radius: 6px`
- `color: var(--text-primary)`

### Inactive State
- `background: transparent`
- `color: var(--text-secondary)`
- Icon opacity: 0.6
- No underline, no text-decoration

### Group Headers
- Uppercase, `var(--text-muted)`, 11px, letter-spacing 1px
- No changes from current behavior

### Spacing
- Link padding: `10px 12px` (up from 8px) for larger click targets
- Nested items: `padding-left: 32px` (unchanged)

### Files

- `Components/Layout/Sidebar.razor.css` — update `.sidebar-link` styles

---

## 3. Dependency Update Buttons

### Per-Dependency "Update" Button

- Appears in the Actions column when `Status == DependencyStatus.UpdateAvailable`
- Calls existing `DependencyManagerService.DownloadAndInstallAsync()`
- Button text: "Update" with download icon
- While updating: disabled with spinner, text "Updating..."
- On success: row refreshes, status shows "Up to Date"
- On error: inline error message with retry option

### "Update All" Button

- New button in the toolbar, next to existing "Check All"
- Text: "Update All" with count badge showing number of available updates
- Disabled when no updates are available
- On click: updates all dependencies with `UpdateAvailable` status sequentially
- Progress feedback: "Updating 1 of N..." status message
- On completion: success/error summary

### Flow

1. User clicks "Check All" → dependencies checked, statuses updated
2. Dependencies with updates show "Update" button + "Update All" becomes active
3. User clicks "Update" on one dependency OR "Update All" for bulk
4. Download → Extract → Verify → Swap (existing pipeline)
5. Status refreshes to "Up to Date" on success

### Files

- `Components/Pages/Settings/DependencyManagement.razor` — add Update/Update All buttons and state management

---

## Out of Scope

- Auto-update toggle (too risky for tools like ADB mid-session)
- Light mode palette changes
- Sidebar collapse behavior changes
- New dependency sources or version check mechanisms
