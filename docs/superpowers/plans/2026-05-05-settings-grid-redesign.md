# Settings Grid Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the flat-list layout on General Settings + Jellyfin Settings with a 2-column grid pattern via three reusable components, add configurable backup/log directories with file migration, and reorder the Settings sub-nav.

**Architecture:** Three small Blazor components (`SettingsSection`, `SettingsGrid`, `SettingsGridCell`) with scoped CSS in `Components/Shared/Settings/`. A new `IJellyfinDirectoryResolver` service resolves backup/log paths from `IConfigurationService` overrides falling back to `OperationLogger`'s derived defaults. Path-change Save handlers run a best-effort `File.Move` migration before persisting the new setting. Behavior changes only on Jellyfin API and Cast & Crew Notifications (per-field auto-save → single Save button per section).

**Tech Stack:** Blazor Server (.NET 9), xUnit + Moq + bUnit (added in Task 1), scoped Razor CSS, Bootstrap Icons (`bi-floppy`, `bi-arrow-repeat`).

**Spec:** `docs/superpowers/specs/2026-05-05-settings-grid-redesign-design.md`

---

## File structure

**New files:**
- `src/ControlMenu/Components/Shared/Settings/SettingsSection.razor` (+ `.razor.css`)
- `src/ControlMenu/Components/Shared/Settings/SettingsGrid.razor` (+ `.razor.css`)
- `src/ControlMenu/Components/Shared/Settings/SettingsGridCell.razor` (+ `.razor.css`)
- `src/ControlMenu/Modules/Jellyfin/Services/IJellyfinDirectoryResolver.cs`
- `src/ControlMenu/Modules/Jellyfin/Services/JellyfinDirectoryResolver.cs`
- `src/ControlMenu/Modules/Jellyfin/Services/DirectoryMigrationResult.cs`
- `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsSectionTests.cs`
- `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridTests.cs`
- `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridCellTests.cs`
- `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinDirectoryResolverTests.cs`

**Modified files:**
- `tests/ControlMenu.Tests/ControlMenu.Tests.csproj` (add bUnit)
- `src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs` (overload `Create()` to accept a log directory)
- `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs` (use resolver for backup paths)
- `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs` (DI registration of the resolver)
- `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor` (rewrite to use components)
- `src/ControlMenu/Components/Pages/Settings/JellyfinSettingsSection.razor` (rewrite to use components, new section, new save handlers, migration calls)
- `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor` (sub-nav reorder)
- `docs/manual-test-checklist.md` (new entries)
- `CHANGELOG.md` (`[Unreleased]` entries)

---

## Task 1: Add bUnit to test project

**Files:**
- Modify: `tests/ControlMenu.Tests/ControlMenu.Tests.csproj`

- [ ] **Step 1: Add bunit package reference**

Edit `tests/ControlMenu.Tests/ControlMenu.Tests.csproj`. Insert into the existing `<ItemGroup>` containing PackageReferences:

```xml
<PackageReference Include="bunit" Version="1.32.7" />
```

(Place it alphabetically — between `Microsoft.NET.Test.Sdk` and `Moq`.)

- [ ] **Step 2: Restore + build to verify the package resolves**

Run: `dotnet build tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release`
Expected: build succeeds, no warnings about bunit.

- [ ] **Step 3: Run existing test suite to verify no regression**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release --nologo`
Expected: all 339 tests pass (the count from `todo_control_menu.md`'s last consolidation; should match unless drift since 2026-05-05).

- [ ] **Step 4: Commit**

```bash
git add tests/ControlMenu.Tests/ControlMenu.Tests.csproj
git commit -m "test: add bunit package reference for Razor component tests"
```

---

## Task 2: SettingsSection component (TDD)

**Files:**
- Create: `src/ControlMenu/Components/Shared/Settings/SettingsSection.razor`
- Create: `src/ControlMenu/Components/Shared/Settings/SettingsSection.razor.css`
- Create: `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsSectionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsSectionTests.cs`:

```csharp
using Bunit;
using ControlMenu.Components.Shared.Settings;

namespace ControlMenu.Tests.Components.Shared.Settings;

public class SettingsSectionTests : TestContext
{
    [Fact]
    public void Renders_TitleAndChildContent()
    {
        var cut = RenderComponent<SettingsSection>(parameters => parameters
            .Add(p => p.Title, "Email (SMTP)")
            .AddChildContent("<p data-testid=\"body\">hello</p>")
        );

        Assert.Contains("Email (SMTP)", cut.Markup);
        Assert.NotNull(cut.Find("[data-testid=\"body\"]"));
    }

    [Fact]
    public void Renders_TitleAsHeader()
    {
        var cut = RenderComponent<SettingsSection>(parameters => parameters
            .Add(p => p.Title, "General")
            .AddChildContent("<span/>")
        );

        var titleEl = cut.Find(".settings-section-title");
        Assert.Equal("General", titleEl.TextContent.Trim());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~SettingsSectionTests" --nologo`
Expected: FAIL — `SettingsSection` type does not exist.

- [ ] **Step 3: Write SettingsSection.razor**

Create `src/ControlMenu/Components/Shared/Settings/SettingsSection.razor`:

```razor
<div class="settings-section-card">
    <div class="settings-section-title">@Title</div>
    @ChildContent
</div>

@code {
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

- [ ] **Step 4: Write scoped CSS**

Create `src/ControlMenu/Components/Shared/Settings/SettingsSection.razor.css`:

```css
.settings-section-card {
    background: var(--surface-2);
    border: 1px solid var(--border);
    border-radius: 8px;
    overflow: hidden;
    margin-bottom: 1.25rem;
}

.settings-section-title {
    background: var(--surface-3);
    padding: 10px 14px;
    font-weight: 600;
    font-size: 14px;
    border-bottom: 1px solid var(--border);
    letter-spacing: 0.3px;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~SettingsSectionTests" --nologo`
Expected: 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Components/Shared/Settings/SettingsSection.razor src/ControlMenu/Components/Shared/Settings/SettingsSection.razor.css tests/ControlMenu.Tests/Components/Shared/Settings/SettingsSectionTests.cs
git commit -m "feat(settings): add SettingsSection component"
```

---

## Task 3: SettingsGrid component (TDD)

**Files:**
- Create: `src/ControlMenu/Components/Shared/Settings/SettingsGrid.razor`
- Create: `src/ControlMenu/Components/Shared/Settings/SettingsGrid.razor.css`
- Create: `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridTests.cs`:

```csharp
using Bunit;
using ControlMenu.Components.Shared.Settings;

namespace ControlMenu.Tests.Components.Shared.Settings;

public class SettingsGridTests : TestContext
{
    [Fact]
    public void Renders_ChildrenInGridContainer()
    {
        var cut = RenderComponent<SettingsGrid>(parameters => parameters
            .AddChildContent("<div data-testid=\"a\"/><div data-testid=\"b\"/>")
        );

        var grid = cut.Find(".settings-grid");
        Assert.NotNull(grid);
        Assert.NotNull(cut.Find("[data-testid=\"a\"]"));
        Assert.NotNull(cut.Find("[data-testid=\"b\"]"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~SettingsGridTests" --nologo`
Expected: FAIL — `SettingsGrid` type does not exist.

- [ ] **Step 3: Write SettingsGrid.razor**

Create `src/ControlMenu/Components/Shared/Settings/SettingsGrid.razor`:

```razor
<div class="settings-grid">
    @ChildContent
</div>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

- [ ] **Step 4: Write scoped CSS**

Create `src/ControlMenu/Components/Shared/Settings/SettingsGrid.razor.css`:

```css
.settings-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~SettingsGridTests" --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Components/Shared/Settings/SettingsGrid.razor src/ControlMenu/Components/Shared/Settings/SettingsGrid.razor.css tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridTests.cs
git commit -m "feat(settings): add SettingsGrid 2-column container"
```

---

## Task 4: SettingsGridCell component (TDD)

**Files:**
- Create: `src/ControlMenu/Components/Shared/Settings/SettingsGridCell.razor`
- Create: `src/ControlMenu/Components/Shared/Settings/SettingsGridCell.razor.css`
- Create: `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridCellTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridCellTests.cs`:

```csharp
using Bunit;
using ControlMenu.Components.Shared.Settings;

namespace ControlMenu.Tests.Components.Shared.Settings;

public class SettingsGridCellTests : TestContext
{
    [Fact]
    public void Renders_AllSlotsWhenProvided()
    {
        var cut = RenderComponent<SettingsGridCell>(parameters => parameters
            .Add(p => p.Label, b => b.AddContent(0, "Theme"))
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<button data-testid=\"ctl\">go</button>"))
            .Add(p => p.Hint, b => b.AddContent(0, "extra info"))
        );

        Assert.Contains("Theme", cut.Find(".settings-grid-cell-label").TextContent);
        Assert.NotNull(cut.Find("[data-testid=\"ctl\"]"));
        Assert.Contains("extra info", cut.Find(".settings-grid-cell-hint").TextContent);
    }

    [Fact]
    public void OmitsLabelHeader_WhenLabelSlotEmpty()
    {
        var cut = RenderComponent<SettingsGridCell>(parameters => parameters
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<button>save</button>"))
        );

        Assert.Empty(cut.FindAll(".settings-grid-cell-label"));
    }

    [Fact]
    public void OmitsHint_WhenHintSlotEmpty()
    {
        var cut = RenderComponent<SettingsGridCell>(parameters => parameters
            .Add(p => p.Label, b => b.AddContent(0, "X"))
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<input/>"))
        );

        Assert.Empty(cut.FindAll(".settings-grid-cell-hint"));
    }

    [Fact]
    public void FullRow_AppliesGridColumnSpan()
    {
        var cut = RenderComponent<SettingsGridCell>(parameters => parameters
            .Add(p => p.FullRow, true)
            .Add(p => p.ChildContent, b => b.AddMarkupContent(0, "<input/>"))
        );

        var cell = cut.Find(".settings-grid-cell");
        Assert.Contains("settings-grid-cell-full", cell.GetAttribute("class") ?? "");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~SettingsGridCellTests" --nologo`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Write SettingsGridCell.razor**

Create `src/ControlMenu/Components/Shared/Settings/SettingsGridCell.razor`:

```razor
<div class="settings-grid-cell @(FullRow ? "settings-grid-cell-full" : "")">
    @if (Label is not null)
    {
        <span class="settings-grid-cell-label">@Label</span>
    }
    @ChildContent
    @if (Hint is not null)
    {
        <span class="settings-grid-cell-hint">@Hint</span>
    }
</div>

@code {
    [Parameter] public RenderFragment? Label { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Hint { get; set; }
    [Parameter] public bool FullRow { get; set; }
}
```

- [ ] **Step 4: Write scoped CSS**

Create `src/ControlMenu/Components/Shared/Settings/SettingsGridCell.razor.css`:

```css
.settings-grid-cell {
    padding: 12px 14px;
    border-top: 1px solid var(--border);
    border-right: 1px solid var(--border);
}

/* Cells in last column lose the right border (handled by sibling positioning).
   Last visual column always has no right border to match the section card edge. */
.settings-grid-cell:nth-child(2n) {
    border-right: none;
}

/* Full-row cells span both columns; their right border is the section edge */
.settings-grid-cell-full {
    grid-column: 1 / -1;
    border-right: none;
}

/* First-row cells (top row of the grid) don't get a top divider */
.settings-grid-cell:nth-child(-n+2) {
    border-top: none;
}
.settings-grid-cell-full:first-child {
    border-top: none;
}

.settings-grid-cell-label {
    display: block;
    font-size: 12px;
    color: var(--muted);
    margin-bottom: 4px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

.settings-grid-cell-hint {
    display: block;
    font-size: 11px;
    color: var(--muted);
    margin-top: 4px;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~SettingsGridCellTests" --nologo`
Expected: 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Components/Shared/Settings/SettingsGridCell.razor src/ControlMenu/Components/Shared/Settings/SettingsGridCell.razor.css tests/ControlMenu.Tests/Components/Shared/Settings/SettingsGridCellTests.cs
git commit -m "feat(settings): add SettingsGridCell with Label/Hint slots and FullRow support"
```

---

## Task 5: Rewrite GeneralSettings.razor against the new components

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor`

This task is markup-only — no behavior changes, no new tests (existing manual checklist covers behavior).

- [ ] **Step 1: Replace the General section**

Replace the contents of the first `<div class="settings-section">…</div>` block (lines 7–67 in the current file — the General section) with:

```razor
<SettingsSection Title="General">
    <SettingsGrid>
        <SettingsGridCell>
            <Label>Setup Wizard</Label>
            <ChildContent>
                <button class="btn btn-secondary" @onclick="RerunWizard">
                    <i class="bi bi-arrow-counterclockwise"></i> Re-run Setup Wizard
                </button>
            </ChildContent>
            <Hint>Walk through the initial setup again.</Hint>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>Timezone</Label>
            <ChildContent>
                <select class="form-control" value="@_timezone" @onchange="SaveTimezone">
                    <option value="UTC">UTC (±00:00)</option>
                    <option value="-12:00">UTC−12:00</option>
                    <option value="-11:00">UTC−11:00</option>
                    <option value="-10:00">UTC−10:00 (Hawaii)</option>
                    <option value="-09:00">UTC−09:00 (Alaska)</option>
                    <option value="-08:00">UTC−08:00 (Pacific)</option>
                    <option value="-07:00">UTC−07:00 (Mountain)</option>
                    <option value="-06:00">UTC−06:00 (Central)</option>
                    <option value="-05:00">UTC−05:00 (Eastern)</option>
                    <option value="-04:00">UTC−04:00 (Atlantic)</option>
                    <option value="-03:30">UTC−03:30 (Newfoundland)</option>
                    <option value="-03:00">UTC−03:00</option>
                    <option value="-02:00">UTC−02:00</option>
                    <option value="-01:00">UTC−01:00</option>
                    <option value="+01:00">UTC+01:00 (Central Europe)</option>
                    <option value="+02:00">UTC+02:00 (Eastern Europe)</option>
                    <option value="+03:00">UTC+03:00 (Moscow)</option>
                    <option value="+03:30">UTC+03:30 (Iran)</option>
                    <option value="+04:00">UTC+04:00 (Gulf)</option>
                    <option value="+04:30">UTC+04:30 (Afghanistan)</option>
                    <option value="+05:00">UTC+05:00 (Pakistan)</option>
                    <option value="+05:30">UTC+05:30 (India)</option>
                    <option value="+05:45">UTC+05:45 (Nepal)</option>
                    <option value="+06:00">UTC+06:00 (Bangladesh)</option>
                    <option value="+06:30">UTC+06:30 (Myanmar)</option>
                    <option value="+07:00">UTC+07:00 (Indochina)</option>
                    <option value="+08:00">UTC+08:00 (China/Singapore)</option>
                    <option value="+09:00">UTC+09:00 (Japan/Korea)</option>
                    <option value="+09:30">UTC+09:30 (Australia Central)</option>
                    <option value="+10:00">UTC+10:00 (Australia Eastern)</option>
                    <option value="+11:00">UTC+11:00</option>
                    <option value="+12:00">UTC+12:00 (New Zealand)</option>
                    <option value="+13:00">UTC+13:00</option>
                </select>
            </ChildContent>
            <Hint>Timezone used for log timestamps. Defaults to UTC.</Hint>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>Theme</Label>
            <ChildContent>
                <div style="display:flex;gap:8px;align-items:center;">
                    <button class="btn @(_theme == "dark" ? "btn-primary" : "btn-secondary")" @onclick='() => SetTheme("dark")'>
                        <i class="bi bi-moon-fill"></i> Dark
                    </button>
                    <button class="btn @(_theme == "light" ? "btn-primary" : "btn-secondary")" @onclick='() => SetTheme("light")'>
                        <i class="bi bi-sun-fill"></i> Light
                    </button>
                </div>
            </ChildContent>
            <Hint>Also available from the icon in the top-right of every page.</Hint>
        </SettingsGridCell>

        <SettingsGridCell />
    </SettingsGrid>
</SettingsSection>
```

- [ ] **Step 2: Replace the Email (SMTP) section**

Replace the second `<div class="settings-section">…</div>` block (Email/SMTP) with:

```razor
<SettingsSection Title="Email (SMTP)">
    <SettingsGrid>
        <SettingsGridCell>
            <Label>SMTP Server</Label>
            <ChildContent>
                <input class="form-control" value="@_smtpServer" placeholder="mail.smtp2go.com" @onchange="SaveSmtpServer" />
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>SMTP Port</Label>
            <ChildContent>
                <input type="number" class="form-control" value="@_smtpPort" @onchange="SaveSmtpPort" />
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>Username</Label>
            <ChildContent>
                <input class="form-control" value="@_smtpUsername" @onchange="SaveSmtpUsername" />
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>Password</Label>
            <ChildContent>
                <input type="password" class="form-control" value="@_smtpPassword" @onchange="SaveSmtpPassword" />
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>From Email</Label>
            <ChildContent>
                <input type="email" class="form-control" value="@_fromEmail" placeholder="noreply@yourdomain.com" @onchange="SaveFromEmail" />
            </ChildContent>
            <Hint>Sender address for outgoing emails. Must be authorized by your SMTP provider.</Hint>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>Notification Email</Label>
            <ChildContent>
                <input type="email" class="form-control" value="@_notificationEmail" placeholder="you@example.com" @onchange="SaveNotificationEmail" />
            </ChildContent>
            <Hint>Default recipient for all notification emails.</Hint>
        </SettingsGridCell>

        <SettingsGridCell>
            <ChildContent>
                <button class="btn btn-secondary" @onclick="SendTestEmail" disabled="@_sendingTest">
                    <i class="bi bi-envelope"></i> @(_sendingTest ? "Sending..." : "Send Test Email")
                </button>
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell />
    </SettingsGrid>
</SettingsSection>
```

- [ ] **Step 3: Replace the ws-scrcpy-web section**

Replace the third `<div class="settings-section">…</div>` block (ws-scrcpy-web deployment) with:

```razor
<SettingsSection Title="ws-scrcpy-web deployment">
    <SettingsGrid>
        <SettingsGridCell>
            <Label>Deployment Mode</Label>
            <ChildContent>
                <div class="radio-group">
                    <label>
                        <input type="radio" name="wsscrcpyMode" value="managed"
                               checked="@(_wsscrcpyMode == "managed")"
                               @onchange="SetModeManaged" />
                        <strong>Managed</strong> — Control Menu spawns and watches the node process on port 8000.
                    </label>
                    <label>
                        <input type="radio" name="wsscrcpyMode" value="external"
                               checked="@(_wsscrcpyMode == "external")"
                               @onchange="SetModeExternal" />
                        <strong>External</strong> — Connect to a running ws-scrcpy-web at the External URL.
                    </label>
                </div>
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>External URL</Label>
            <ChildContent>
                <input class="form-control"
                       @bind="_wsscrcpyUrl" @bind:event="onchange"
                       @onblur="SaveUrl"
                       disabled="@(_wsscrcpyMode != "external")" />
            </ChildContent>
            <Hint>e.g. <code>http://localhost:8000</code> or <code>http://ws-scrcpy:8000</code>. Disabled until External mode is selected.</Hint>
        </SettingsGridCell>
    </SettingsGrid>
</SettingsSection>
```

- [ ] **Step 4: Verify the @code block is unchanged**

The `@code { ... }` block at the bottom of the file (lines ~155–291 in the current file) stays exactly as-is. All save handlers, `OnInitializedAsync`, `ShowMessage`, `RerunWizard`, `SetWsScrcpyMode`, `SaveUrl` — unchanged. Verify by inspecting that no method names changed.

- [ ] **Step 5: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: build succeeds.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release --nologo`
Expected: all tests pass (existing 339 + 7 new component tests = 346).

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/GeneralSettings.razor
git commit -m "refactor(settings): rewrite GeneralSettings against SettingsGrid components

Reorder General section: Re-run Wizard, Timezone, Theme. Theme cell
gains hint about the global top-right toggle. ws-scrcpy URL cell always
renders, just toggles disabled. No behavior changes."
```

---

## Task 6: Introduce IJellyfinDirectoryResolver service

**Files:**
- Create: `src/ControlMenu/Modules/Jellyfin/Services/IJellyfinDirectoryResolver.cs`
- Create: `src/ControlMenu/Modules/Jellyfin/Services/JellyfinDirectoryResolver.cs`
- Create: `src/ControlMenu/Modules/Jellyfin/Services/DirectoryMigrationResult.cs`
- Create: `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinDirectoryResolverTests.cs`
- Modify: `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinDirectoryResolverTests.cs`:

```csharp
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Modules.Jellyfin.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Jellyfin;

public class JellyfinDirectoryResolverTests
{
    private static Mock<IConfigurationService> ConfigReturning(
        string? backupOverride = null,
        string? logOverride = null)
    {
        var mock = new Mock<IConfigurationService>();
        mock.Setup(c => c.GetSettingAsync("jellyfin-backup-directory", null))
            .ReturnsAsync(backupOverride);
        mock.Setup(c => c.GetSettingAsync("jellyfin-log-directory", null))
            .ReturnsAsync(logOverride);
        return mock;
    }

    [Fact]
    public async Task GetBackupDirectory_NoOverride_ReturnsDefault()
    {
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object);
        var dir = await resolver.GetBackupDirectoryAsync();
        Assert.Equal(OperationLogger.GetDefaultBackupDirectory(), dir);
    }

    [Fact]
    public async Task GetBackupDirectory_OverrideSet_ReturnsOverride()
    {
        var resolver = new JellyfinDirectoryResolver(ConfigReturning(backupOverride: "D:\\custom\\backups").Object);
        var dir = await resolver.GetBackupDirectoryAsync();
        Assert.Equal("D:\\custom\\backups", dir);
    }

    [Fact]
    public async Task GetLogDirectory_NoOverride_ReturnsDefault()
    {
        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object);
        var dir = await resolver.GetLogDirectoryAsync();
        Assert.Equal(OperationLogger.GetDefaultLogDirectory(), dir);
    }

    [Fact]
    public async Task GetLogDirectory_OverrideSet_ReturnsOverride()
    {
        var resolver = new JellyfinDirectoryResolver(ConfigReturning(logOverride: "D:\\custom\\logs").Object);
        var dir = await resolver.GetLogDirectoryAsync();
        Assert.Equal("D:\\custom\\logs", dir);
    }

    [Fact]
    public async Task GetBackupDirectory_OverrideEmpty_ReturnsDefault()
    {
        var resolver = new JellyfinDirectoryResolver(ConfigReturning(backupOverride: "").Object);
        var dir = await resolver.GetBackupDirectoryAsync();
        Assert.Equal(OperationLogger.GetDefaultBackupDirectory(), dir);
    }

    [Fact]
    public async Task MigrateBackupDirectory_NoOldFiles_ReturnsZeroMoved()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        var newDir = Path.Combine(temp.Path, "new");
        Directory.CreateDirectory(oldDir);

        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object);
        var result = await resolver.MigrateFilesAsync(oldDir, newDir, "*.db");

        Assert.Equal(0, result.MovedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public async Task MigrateBackupDirectory_HappyPath_AllFilesMoved()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        var newDir = Path.Combine(temp.Path, "new");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "a.db"), "x");
        File.WriteAllText(Path.Combine(oldDir, "b.db"), "y");

        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object);
        var result = await resolver.MigrateFilesAsync(oldDir, newDir, "*.db");

        Assert.Equal(2, result.MovedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(File.Exists(Path.Combine(newDir, "a.db")));
        Assert.True(File.Exists(Path.Combine(newDir, "b.db")));
        Assert.Empty(Directory.GetFiles(oldDir, "*.db"));
    }

    [Fact]
    public async Task MigrateBackupDirectory_TargetDirNotCreatable_ReturnsError()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        Directory.CreateDirectory(oldDir);
        // Path with NUL char on Windows is invalid and cannot be created
        var badNewDir = Path.Combine(temp.Path, "bad\0path");

        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object);
        var result = await resolver.MigrateFilesAsync(oldDir, badNewDir, "*.db");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task MigrateBackupDirectory_OneFileLocked_ReturnsPartialResult()
    {
        using var temp = new TempDir();
        var oldDir = Path.Combine(temp.Path, "old");
        var newDir = Path.Combine(temp.Path, "new");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "a.db"), "x");
        var lockedPath = Path.Combine(oldDir, "locked.db");
        File.WriteAllText(lockedPath, "y");

        // Hold an exclusive lock on locked.db
        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var resolver = new JellyfinDirectoryResolver(ConfigReturning().Object);
        var result = await resolver.MigrateFilesAsync(oldDir, newDir, "*.db");

        Assert.Equal(1, result.MovedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.FailedFiles);
        Assert.Equal("locked.db", result.FailedFiles[0]);
        Assert.True(File.Exists(Path.Combine(newDir, "a.db")));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~JellyfinDirectoryResolverTests" --nologo`
Expected: FAIL — types do not exist; `OperationLogger.GetDefaultBackupDirectory` / `GetDefaultLogDirectory` do not exist yet.

- [ ] **Step 3: Rename OperationLogger statics to "Default"**

Edit `src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs`:

Replace:
```csharp
public static string GetLogDirectory() =>
    Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "logging");

public static string GetBackupDirectory()
{
    var dir = Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "backups");
    Directory.CreateDirectory(dir);
    return dir;
}
```

with:

```csharp
public static string GetDefaultLogDirectory() =>
    Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "logging");

public static string GetDefaultBackupDirectory()
{
    var dir = Path.Combine(AppContext.BaseDirectory, "jellyfin-data", "backups");
    Directory.CreateDirectory(dir);
    return dir;
}
```

Update the internal call site at line 68 (`GetRecentLogs`):
```csharp
var logDir = GetDefaultLogDirectory();
```

(Note: `GetRecentLogs` reads from the *default* directory only — if the user has overridden the log path, the logs already live elsewhere. Recent-logs UI in the Jellyfin module historically points at the default location; out of scope for this redesign to migrate.)

- [ ] **Step 4: Create DirectoryMigrationResult**

Create `src/ControlMenu/Modules/Jellyfin/Services/DirectoryMigrationResult.cs`:

```csharp
namespace ControlMenu.Modules.Jellyfin.Services;

public sealed record DirectoryMigrationResult(
    bool Success,
    int MovedCount,
    int FailedCount,
    IReadOnlyList<string> FailedFiles,
    string? ErrorMessage)
{
    public static DirectoryMigrationResult Ok(int movedCount, IReadOnlyList<string> failedFiles) =>
        new(Success: true, MovedCount: movedCount, FailedCount: failedFiles.Count, FailedFiles: failedFiles, ErrorMessage: null);

    public static DirectoryMigrationResult Error(string message) =>
        new(Success: false, MovedCount: 0, FailedCount: 0, FailedFiles: [], ErrorMessage: message);
}
```

- [ ] **Step 5: Create IJellyfinDirectoryResolver**

Create `src/ControlMenu/Modules/Jellyfin/Services/IJellyfinDirectoryResolver.cs`:

```csharp
namespace ControlMenu.Modules.Jellyfin.Services;

public interface IJellyfinDirectoryResolver
{
    Task<string> GetBackupDirectoryAsync();
    Task<string> GetLogDirectoryAsync();

    /// <summary>
    /// Best-effort move of files matching <paramref name="searchPattern"/> from
    /// <paramref name="oldDir"/> to <paramref name="newDir"/>. Creates <paramref name="newDir"/>
    /// if missing. Per-file failures (e.g. file locks) do not abort the batch.
    /// </summary>
    Task<DirectoryMigrationResult> MigrateFilesAsync(string oldDir, string newDir, string searchPattern);
}
```

- [ ] **Step 6: Create JellyfinDirectoryResolver implementation**

Create `src/ControlMenu/Modules/Jellyfin/Services/JellyfinDirectoryResolver.cs`:

```csharp
using ControlMenu.Services;

namespace ControlMenu.Modules.Jellyfin.Services;

public sealed class JellyfinDirectoryResolver : IJellyfinDirectoryResolver
{
    private const string BackupDirectoryKey = "jellyfin-backup-directory";
    private const string LogDirectoryKey = "jellyfin-log-directory";

    private readonly IConfigurationService _config;

    public JellyfinDirectoryResolver(IConfigurationService config)
    {
        _config = config;
    }

    public async Task<string> GetBackupDirectoryAsync()
    {
        var overridePath = await _config.GetSettingAsync(BackupDirectoryKey);
        return string.IsNullOrWhiteSpace(overridePath)
            ? OperationLogger.GetDefaultBackupDirectory()
            : overridePath;
    }

    public async Task<string> GetLogDirectoryAsync()
    {
        var overridePath = await _config.GetSettingAsync(LogDirectoryKey);
        return string.IsNullOrWhiteSpace(overridePath)
            ? OperationLogger.GetDefaultLogDirectory()
            : overridePath;
    }

    public Task<DirectoryMigrationResult> MigrateFilesAsync(string oldDir, string newDir, string searchPattern)
    {
        try
        {
            Directory.CreateDirectory(newDir);
        }
        catch (Exception ex)
        {
            return Task.FromResult(DirectoryMigrationResult.Error(
                $"Could not create target directory: {ex.Message}"));
        }

        if (!Directory.Exists(oldDir))
        {
            return Task.FromResult(DirectoryMigrationResult.Ok(movedCount: 0, failedFiles: []));
        }

        var moved = 0;
        var failed = new List<string>();

        foreach (var src in Directory.GetFiles(oldDir, searchPattern))
        {
            var name = Path.GetFileName(src);
            var dst = Path.Combine(newDir, name);
            try
            {
                if (File.Exists(dst))
                {
                    // Same-named file already present at destination — skip and report as failed.
                    failed.Add(name);
                    continue;
                }
                File.Move(src, dst);
                moved++;
            }
            catch (IOException)
            {
                failed.Add(name);
            }
            catch (UnauthorizedAccessException)
            {
                failed.Add(name);
            }
        }

        return Task.FromResult(DirectoryMigrationResult.Ok(moved, failed));
    }
}
```

- [ ] **Step 7: Register in DI**

Edit `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs`. Find the existing service registrations (`services.AddScoped<...>()` or `services.AddSingleton<...>()` calls registering `JellyfinService` etc.) and add alongside them:

```csharp
services.AddScoped<IJellyfinDirectoryResolver, JellyfinDirectoryResolver>();
```

If the file does not have a clear DI hook, search for `JellyfinService` registration in `Program.cs` or the module's `ConfigureServices` and add the line there.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~JellyfinDirectoryResolverTests" --nologo`
Expected: all 9 tests PASS.

- [ ] **Step 9: Verify full build (callers of renamed methods need updating, see Task 7)**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: BUILD ERRORS in `JellyfinService.cs:76`, `JellyfinService.cs:130`, `JellyfinSettingsSection.razor` (lines 37, 90, 95, 204, 212) — they all call `OperationLogger.GetBackupDirectory()` / `GetLogDirectory()` which were renamed. **This is expected** — Task 7 fixes JellyfinService callers, Task 9 fixes the Razor callers.

- [ ] **Step 10: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Services/IJellyfinDirectoryResolver.cs src/ControlMenu/Modules/Jellyfin/Services/JellyfinDirectoryResolver.cs src/ControlMenu/Modules/Jellyfin/Services/DirectoryMigrationResult.cs src/ControlMenu/Modules/Jellyfin/Services/OperationLogger.cs src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinDirectoryResolverTests.cs
git commit -m "feat(jellyfin): add IJellyfinDirectoryResolver with override + migration

Renames OperationLogger.GetBackupDirectory/GetLogDirectory to
GetDefaultBackupDirectory/GetDefaultLogDirectory. New resolver consults
IConfigurationService for jellyfin-backup-directory and
jellyfin-log-directory overrides; falls back to defaults. Includes
best-effort MigrateFilesAsync for path-change scenarios."
```

---

## Task 7: Update JellyfinService backup-path callers

**Files:**
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`

The service currently calls `OperationLogger.GetBackupDirectory()` at lines 76 and 130. Both need to use the resolver.

- [ ] **Step 1: Inject IJellyfinDirectoryResolver into JellyfinService**

Edit `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`. Find the constructor (it likely takes `IConfigurationService` and a few others). Add `IJellyfinDirectoryResolver directoryResolver` as a parameter and store it in a private field.

```csharp
private readonly IJellyfinDirectoryResolver _directoryResolver;

public JellyfinService(
    /* existing parameters... */,
    IJellyfinDirectoryResolver directoryResolver)
{
    /* existing assignments... */
    _directoryResolver = directoryResolver;
}
```

- [ ] **Step 2: Replace static calls with resolver calls**

Find each occurrence:

**Line 76** (`BackupDatabaseAsync`):
```csharp
var backupDir = OperationLogger.GetBackupDirectory();
```
Replace with:
```csharp
var backupDir = await _directoryResolver.GetBackupDirectoryAsync();
Directory.CreateDirectory(backupDir);
```

**Line 130** (`CleanupOldBackupsAsync`):
```csharp
var backupDir = OperationLogger.GetBackupDirectory();
```
Replace with:
```csharp
var backupDir = await _directoryResolver.GetBackupDirectoryAsync();
```

(Don't add `CreateDirectory` here — cleanup against a non-existent dir should no-op naturally.)

- [ ] **Step 3: Run JellyfinService tests**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter "FullyQualifiedName~JellyfinServiceTests" --nologo`
Expected: tests likely fail because the constructor signature changed and existing tests don't pass the new parameter.

- [ ] **Step 4: Update JellyfinServiceTests to provide the new dependency**

Open `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs`. Find any place where `new JellyfinService(...)` is called. Pass a Mock<IJellyfinDirectoryResolver> as the new argument. For each test setup, configure the mock to return a per-test temp directory:

```csharp
var resolverMock = new Mock<IJellyfinDirectoryResolver>();
resolverMock.Setup(r => r.GetBackupDirectoryAsync())
    .ReturnsAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
```

Pass `resolverMock.Object` to the JellyfinService constructor wherever it's instantiated.

- [ ] **Step 5: Run the test suite**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release --nologo`
Expected: all tests pass.

- [ ] **Step 6: Verify the app builds**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: build still has errors in `JellyfinSettingsSection.razor` (Task 9 fixes those). No new errors introduced by this task.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs
git commit -m "refactor(jellyfin): JellyfinService uses IJellyfinDirectoryResolver for backup paths"
```

---

## Task 8: Settings sub-nav reorder

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor`

Small isolated change; doing it before Task 9 (the big Jellyfin rewrite) so Task 9 can verify against the new sub-nav.

- [ ] **Step 1: Reorder buttons**

Edit `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor` lines 8–27. New button order: General, Jellyfin, Android Devices, Cameras, Dependencies. Replace the entire `<nav class="settings-nav">` block with:

```razor
<nav class="settings-nav">
    <button class="settings-nav-item @(ActiveSection == "general" ? "active" : "")"
            @onclick='() => Navigate("general")'>
        <i class="bi bi-gear"></i> General
    </button>
    <button class="settings-nav-item @(ActiveSection == "jellyfin" ? "active" : "")"
            @onclick='() => Navigate("jellyfin")'>
        <i class="bi bi-film"></i> Jellyfin
    </button>
    <button class="settings-nav-item @(ActiveSection == "devices" ? "active" : "")"
            @onclick='() => Navigate("devices")'>
        <i class="bi bi-phone"></i> Android Devices
    </button>
    <button class="settings-nav-item @(ActiveSection == "cameras" ? "active" : "")"
            @onclick='() => Navigate("cameras")'>
        <i class="bi bi-camera-video"></i> Cameras
    </button>
    <button class="settings-nav-item @(ActiveSection == "dependencies" ? "active" : "")"
            @onclick='() => Navigate("dependencies")'>
        <i class="bi bi-box-seam"></i> Dependencies
    </button>
</nav>
```

The `@switch` block below the nav stays unchanged — routes still resolve identically.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: build still has errors in `JellyfinSettingsSection.razor` (Task 9 fixes those). No new errors.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/SettingsPage.razor
git commit -m "refactor(settings): reorder Settings sub-nav, Jellyfin moves to position 2"
```

---

## Task 9: Rewrite JellyfinSettingsSection — first three sections

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/JellyfinSettingsSection.razor`

This task handles Docker Compose, Jellyfin API, and Cast & Crew Notifications — the sections that don't need migration logic. Task 10 follows up with the Logging/Backup/Retention table.

- [ ] **Step 1: Update injections and add new state fields**

Edit the `@code { ... }` block. Replace:

```csharp
[Inject] private IConfigurationService Config { get; set; } = default!;
[Inject] private IJellyfinService JellyfinService { get; set; } = default!;
```

with:

```csharp
[Inject] private IConfigurationService Config { get; set; } = default!;
[Inject] private IJellyfinService JellyfinService { get; set; } = default!;
[Inject] private IJellyfinDirectoryResolver DirectoryResolver { get; set; } = default!;
```

Add `using ControlMenu.Modules.Jellyfin.Services;` at the top of the `@using` block if not already present.

- [ ] **Step 2: Replace per-field auto-save handlers with a single SaveJellyfinApi handler**

Remove the three handlers `SaveBaseUrl`, `SaveApiKey`, `SaveUserId` from the `@code` block. Add:

```csharp
private async Task SaveJellyfinApi()
{
    await Config.SetSettingAsync("jellyfin-base-url", _baseUrl);

    if (!string.IsNullOrEmpty(_apiKey))
    {
        await Config.SetSecretAsync("jellyfin-api-key", _apiKey);
    }
    else
    {
        await Config.DeleteSettingAsync("jellyfin-api-key");
    }

    await Config.SetSettingAsync("jellyfin-user-id", _userId);
    ShowMessage("Jellyfin API saved.", false);
}
```

- [ ] **Step 3: Replace SaveCastCrewEmail signature**

The existing `SaveCastCrewEmail(ChangeEventArgs e)` becomes button-triggered. Replace with:

```csharp
private async Task SaveCastCrewEmail()
{
    await Config.SetSettingAsync("jellyfin-castcrew-notify-email", _castCrewEmail);
    ShowMessage("Notification email saved.", false);
}
```

- [ ] **Step 4: Rewrite the Docker Compose section**

Replace the first `<div class="settings-section">…</div>` block (Docker Compose, lines 4–42 in the current file) with:

```razor
<SettingsSection Title="Docker Compose">
    <div class="settings-section-intro">
        Point Control Menu to your Jellyfin docker-compose.yml to auto-detect container name and database path.
    </div>

    <SettingsGrid>
        <SettingsGridCell FullRow="true">
            <Label>Compose File Path</Label>
            <ChildContent>
                <div style="display:flex;gap:8px;align-items:center;">
                    <input type="text" class="form-control" @bind="_composePath" placeholder="e.g., D:\DockerData\jellyfin\docker-compose.yml" style="flex:1;" />
                    <button class="btn btn-primary" @onclick="SaveAndParse" disabled="@_parsing">
                        <i class="bi bi-arrow-repeat"></i> @(_parsing ? "Parsing..." : "Save & Parse")
                    </button>
                </div>

                @if (_parseResult is not null)
                {
                    @if (_parseResult.ErrorMessage is not null)
                    {
                        <div class="alert alert-danger" style="margin-top:8px;">
                            <i class="bi bi-exclamation-triangle"></i> @_parseResult.ErrorMessage
                        </div>
                    }
                    else
                    {
                        <table class="data-table" style="max-width:600px;margin-top:8px;">
                            <tr>
                                <td><strong>Container Name</strong></td>
                                <td><code>@(_parseResult.ContainerName ?? "—")</code></td>
                            </tr>
                            <tr>
                                <td><strong>Database Path</strong></td>
                                <td><code>@(_parseResult.DbPath ?? "—")</code></td>
                            </tr>
                        </table>
                    }
                }
            </ChildContent>
        </SettingsGridCell>
    </SettingsGrid>
</SettingsSection>
```

(The Backup Directory row in the parse-result table is intentionally omitted — that's the redundancy fix.)

- [ ] **Step 5: Rewrite the Jellyfin API section**

Replace the second `<div class="settings-section">…</div>` block (Jellyfin API) with:

```razor
<SettingsSection Title="Jellyfin API">
    <SettingsGrid>
        <SettingsGridCell>
            <Label>Base URL</Label>
            <ChildContent>
                <input class="form-control" @bind="_baseUrl" placeholder="http://127.0.0.1:8096" />
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>API Key</Label>
            <ChildContent>
                <input type="password" class="form-control" @bind="_apiKey" />
            </ChildContent>
        </SettingsGridCell>

        <SettingsGridCell>
            <Label>User ID</Label>
            <ChildContent>
                <input class="form-control" @bind="_userId" />
            </ChildContent>
            <Hint>Jellyfin user ID for API calls (used by Cast &amp; Crew updates).</Hint>
        </SettingsGridCell>

        <SettingsGridCell>
            <ChildContent>
                <div style="display:flex;justify-content:flex-end;align-items:flex-end;height:100%;">
                    <button class="btn btn-primary" @onclick="SaveJellyfinApi">
                        <i class="bi bi-floppy"></i> Save Jellyfin API
                    </button>
                </div>
            </ChildContent>
        </SettingsGridCell>
    </SettingsGrid>
</SettingsSection>
```

- [ ] **Step 6: Rewrite the Cast & Crew Notifications section**

Replace the corresponding `<div class="settings-section">` block with:

```razor
<SettingsSection Title="Cast &amp; Crew Notifications">
    <SettingsGrid>
        <SettingsGridCell>
            <Label>Notification Email</Label>
            <ChildContent>
                <input type="email" class="form-control" @bind="_castCrewEmail"
                       placeholder="Uses default from General if blank" />
            </ChildContent>
            <Hint>Receives completion alerts for Cast &amp; Crew updates. Leave blank to use the default notification email from General settings.</Hint>
        </SettingsGridCell>

        <SettingsGridCell>
            <ChildContent>
                <div style="display:flex;justify-content:flex-end;align-items:flex-end;height:100%;">
                    <button class="btn btn-primary" @onclick="SaveCastCrewEmail">
                        <i class="bi bi-floppy"></i> Save Notification Email
                    </button>
                </div>
            </ChildContent>
        </SettingsGridCell>
    </SettingsGrid>
</SettingsSection>
```

- [ ] **Step 7: Add a tiny global style for the section intro paragraph**

Append to `src/ControlMenu/wwwroot/css/app.css` (or the project's main stylesheet — search for `.settings-section` to find it):

```css
.settings-section-intro {
    padding: 10px 14px 0;
    font-size: 13px;
    color: var(--muted);
}
```

- [ ] **Step 8: Verify the @code block compiles**

The remaining `_composePath`, `_baseUrl`, `_apiKey`, `_userId`, `_castCrewEmail`, `_retentionDays`, `_parsing`, `_parseResult`, `_message`, `_messageIsError`, `_backupCount`, `_backupSize`, `_logCount` fields stay. `OnInitializedAsync`, `SaveAndParse`, `SaveRetention`, `RefreshDirectoryStats`, `ShowMessage`, `FormatSize` stay. Methods removed: `SaveBaseUrl`, `SaveApiKey`, `SaveUserId`, original `SaveCastCrewEmail(ChangeEventArgs)`. New methods: `SaveJellyfinApi()`, new `SaveCastCrewEmail()` (no args).

The Backup & Retention section and Managed Directories section markup are still present at this point. Task 10 replaces them.

- [ ] **Step 9: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: BUILD ERRORS only on lines 90 and 95 of `JellyfinSettingsSection.razor` (the `@OperationLogger.GetBackupDirectory()` and `@OperationLogger.GetLogDirectory()` calls in the still-present Managed Directories section). Task 10 fixes those. No other errors.

- [ ] **Step 10: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/JellyfinSettingsSection.razor src/ControlMenu/wwwroot/css/app.css
git commit -m "refactor(jellyfin-settings): rewrite Compose/API/Cast&Crew with grid components

Backup Directory row removed from Compose parse-result table. Jellyfin
API and Cast & Crew Notifications switch from per-field auto-save to a
single Save button per section with the bi-floppy icon."
```

---

## Task 10: Build the Logging, Backup & Retention table

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Settings/JellyfinSettingsSection.razor`

- [ ] **Step 1: Add new state fields and migration handlers in @code**

Add to the field declarations near the top of `@code`:

```csharp
private string _backupDirectory = "";
private string _logDirectory = "";
```

Update `OnInitializedAsync` to load them. Locate the existing block and add immediately after `_retentionDays` is loaded:

```csharp
_backupDirectory = await DirectoryResolver.GetBackupDirectoryAsync();
_logDirectory = await DirectoryResolver.GetLogDirectoryAsync();
```

Add three new save handlers below the existing handlers:

```csharp
private async Task SaveBackupDirectory()
{
    var newPath = (_backupDirectory ?? "").Trim();
    if (string.IsNullOrEmpty(newPath))
    {
        ShowMessage("Backup directory cannot be empty.", true);
        return;
    }

    var oldPath = await DirectoryResolver.GetBackupDirectoryAsync();
    if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
    {
        ShowMessage("Backups path saved.", false);
        return;
    }

    var migration = await DirectoryResolver.MigrateFilesAsync(oldPath, newPath, "*.db");
    if (!migration.Success)
    {
        ShowMessage($"Could not save: {migration.ErrorMessage}", true);
        return;
    }

    await Config.SetSettingAsync("jellyfin-backup-directory", newPath);
    RefreshDirectoryStats();
    ShowMessage(BuildMigrationMessage("Backups", migration), migration.FailedCount > 0);
}

private async Task SaveLogDirectory()
{
    var newPath = (_logDirectory ?? "").Trim();
    if (string.IsNullOrEmpty(newPath))
    {
        ShowMessage("Log directory cannot be empty.", true);
        return;
    }

    var oldPath = await DirectoryResolver.GetLogDirectoryAsync();
    if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
    {
        ShowMessage("Logs path saved.", false);
        return;
    }

    var migration = await DirectoryResolver.MigrateFilesAsync(oldPath, newPath, "*.log");
    if (!migration.Success)
    {
        ShowMessage($"Could not save: {migration.ErrorMessage}", true);
        return;
    }

    await Config.SetSettingAsync("jellyfin-log-directory", newPath);
    RefreshDirectoryStats();
    ShowMessage(BuildMigrationMessage("Logs", migration), migration.FailedCount > 0);
}

private static string BuildMigrationMessage(string label, DirectoryMigrationResult result)
{
    if (result.MovedCount == 0 && result.FailedCount == 0)
        return $"{label} path saved.";
    if (result.FailedCount == 0)
        return $"{label} path saved. Moved {result.MovedCount} files.";
    return $"{label} path saved. Moved {result.MovedCount} files; {result.FailedCount} could not be moved (in use): {string.Join(", ", result.FailedFiles)}. Retry the Save to migrate them.";
}
```

- [ ] **Step 2: Update RefreshDirectoryStats to consult the resolver**

The current implementation reads `OperationLogger.GetBackupDirectory()` / `GetLogDirectory()`. Replace with:

```csharp
private async void RefreshDirectoryStats()
{
    var backupDir = await DirectoryResolver.GetBackupDirectoryAsync();
    if (Directory.Exists(backupDir))
    {
        var files = Directory.GetFiles(backupDir, "*.db");
        _backupCount = files.Length;
        _backupSize = files.Sum(f => new FileInfo(f).Length);
    }
    else
    {
        _backupCount = 0;
        _backupSize = 0;
    }

    var logDir = await DirectoryResolver.GetLogDirectoryAsync();
    if (Directory.Exists(logDir))
    {
        _logCount = Directory.GetFiles(logDir, "*.log").Length;
    }
    else
    {
        _logCount = 0;
    }

    StateHasChanged();
}
```

The signature is now `private async void` (was `private void`) — fire-and-forget for the stats refresh. This matches the existing fire-and-forget pattern used elsewhere on the page for non-blocking refresh.

- [ ] **Step 3: Replace Backup & Retention and Managed Directories sections**

Find the two sections — Backup & Retention (current lines 64–72) and Managed Directories (current lines 85–99). Replace **both** with a single new section:

```razor
<SettingsSection Title="Logging, Backup &amp; Retention">
    <table class="data-table" style="width:100%;">
        <thead>
            <tr>
                <th style="text-align:left;width:130px;">Setting</th>
                <th style="text-align:left;">Value</th>
                <th style="width:90px;"></th>
            </tr>
        </thead>
        <tbody>
            <tr>
                <td><strong>Backups</strong></td>
                <td>
                    <input class="form-control" @bind="_backupDirectory" />
                    <span style="font-size:11px;color:var(--muted);margin-top:4px;display:block;">@_backupCount files, @FormatSize(_backupSize)</span>
                </td>
                <td>
                    <button class="btn btn-primary" @onclick="SaveBackupDirectory">
                        <i class="bi bi-floppy"></i> Save
                    </button>
                </td>
            </tr>
            <tr>
                <td><strong>Logs</strong></td>
                <td>
                    <input class="form-control" @bind="_logDirectory" />
                    <span style="font-size:11px;color:var(--muted);margin-top:4px;display:block;">@_logCount files</span>
                </td>
                <td>
                    <button class="btn btn-primary" @onclick="SaveLogDirectory">
                        <i class="bi bi-floppy"></i> Save
                    </button>
                </td>
            </tr>
            <tr>
                <td><strong>Retention</strong></td>
                <td>
                    <input type="number" class="form-control" @bind="_retentionDays" min="1" max="365" style="width:120px;display:inline-block;" />
                    <span style="margin-left:8px;color:var(--muted);">days</span>
                </td>
                <td>
                    <button class="btn btn-primary" @onclick="SaveRetention">
                        <i class="bi bi-floppy"></i> Save
                    </button>
                </td>
            </tr>
        </tbody>
    </table>
</SettingsSection>
```

- [ ] **Step 4: Update SaveRetention notification icon**

Find the existing `SaveRetention` method. Confirm it still calls `Config.SetSettingAsync("jellyfin-backup-retention-days", _retentionDays.ToString())` and shows a message. No code change beyond what's already there — just verify the message wording is consistent (`"Backup retention set to {n} days."`).

- [ ] **Step 5: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo`
Expected: build succeeds.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release --nologo`
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/JellyfinSettingsSection.razor
git commit -m "feat(jellyfin-settings): combined Logging/Backup/Retention with editable paths

New section replaces Backup & Retention + Managed Directories. Backup
and Log directories become user-configurable; Save migrates existing
*.db / *.log files via JellyfinDirectoryResolver.MigrateFilesAsync.
Partial-failure outcomes surface in the standard notification."
```

---

## Task 11: Manual run-through and start dev server

The components ship without whole-page Razor tests (per the spec). Do a manual end-to-end before the documentation commit.

- [ ] **Step 1: Start the dev server**

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release`
Expected: server starts, http://localhost:5159 reachable.

- [ ] **Step 2: Manual checklist — General page**

Open http://localhost:5159/settings/general. Verify:

- General section: top-row left = Setup Wizard, top-row right = Timezone; second row = Theme, empty.
- Theme cell hint reads "Also available from the icon in the top-right of every page."
- Click Re-run Setup Wizard → returns to wizard; abort and come back.
- Change timezone → notification "Timezone saved."
- Click Light / Dark → theme toggles; notification appears.
- Email (SMTP) section: 4-row grid, Test Email button alone in last row.
- Type SMTP server value, tab away → "Saved." notification.
- ws-scrcpy-web section: Mode radios with inline descriptions; URL field disabled in Managed.
- Switch to External → URL becomes editable; type a URL, blur → "URL saved. Restart to apply."

- [ ] **Step 3: Manual checklist — Settings sub-nav**

In the left rail of /settings, verify order: General, Jellyfin, Android Devices, Cameras, Dependencies. Click each — content swaps without route 404s.

- [ ] **Step 4: Manual checklist — Jellyfin page non-migration sections**

Open /settings/jellyfin. Verify:

- Docker Compose: input + Save & Parse button; parse result table (when populated) shows Container + DB only, no Backup Directory row.
- Jellyfin API: 3 fields + Save Jellyfin API button. Edit a field, click Save → "Jellyfin API saved." All three fields persisted (re-load page, verify values stayed).
- Cast & Crew Notifications: 1 field + Save Notification Email. Edit, Save → "Notification email saved."

- [ ] **Step 5: Manual checklist — Logging, Backup & Retention**

- Verify three rows: Backups (path + stats + Save), Logs (path + stats + Save), Retention (days + Save).
- Change Backups path to a new empty directory (create one in Explorer first). Click Save.
- Expected: notification reads "Backups path saved. Moved {n} files." (assuming pre-existing `.db` files); old directory is empty of `.db`; new directory contains the moved files.
- Change Logs path similarly. Likely partial-failure on locked log file: notification reads "Logs path saved. Moved {n} files; 1 could not be moved (in use): … . Retry the Save to migrate them." Validate format.
- Change Retention to 7, click Save → "Backup retention set to 7 days."

- [ ] **Step 6: Stop the dev server**

Ctrl+C in the dev-server terminal.

- [ ] **Step 7: No commit (manual smoke step only)**

Move on to Task 12.

---

## Task 12: CHANGELOG + manual checklist updates

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/manual-test-checklist.md`

- [ ] **Step 1: Add CHANGELOG entries**

Edit `CHANGELOG.md`. Under `## [Unreleased]` → `### Added`, append:

```markdown
- **Configurable Jellyfin backup and log directories.** New `jellyfin-backup-directory` and `jellyfin-log-directory` settings on the Jellyfin Settings page (Logging, Backup & Retention section). Empty value falls back to the derived defaults under `AppContext.BaseDirectory`. Saving a new path best-effort-migrates existing `*.db` (Backups) or `*.log` (Logs) files; per-file failures (e.g., locked active log files on Windows) surface in the standard notification with a "Retry the Save to migrate them" hint.
- **Reusable Settings grid components** (`SettingsSection`, `SettingsGrid`, `SettingsGridCell`) under `Components/Shared/Settings/`. Two-column grid pattern with optional Label / Hint slots and FullRow span. Adopted by General Settings and Jellyfin Settings; available for future settings pages.
```

Under `### Changed`, append:

```markdown
- **General Settings page rewritten with the SettingsGrid components.** General section reordered (Re-run Setup Wizard, Timezone, Theme); Theme cell gains a hint pointing at the global top-right toggle. Email (SMTP) and ws-scrcpy-web sections render in a 2-column grid; ws-scrcpy URL field always renders, just toggles `disabled` based on Managed/External mode. No behavior changes.
- **Jellyfin Settings page rewritten with the SettingsGrid components.** Section order: Docker Compose, Jellyfin API, Cast & Crew Notifications, Logging/Backup/Retention. Jellyfin API and Cast & Crew now use a single per-section Save button (bi-floppy icon) instead of per-field auto-save.
- **Settings sub-nav reordered.** New order: General, Jellyfin, Android Devices, Cameras, Dependencies (was: General, Android Devices, Cameras, Jellyfin, Dependencies).
- **`OperationLogger.GetBackupDirectory` / `GetLogDirectory` renamed to `GetDefaultBackupDirectory` / `GetDefaultLogDirectory`** to make their fallback nature explicit. Override-aware path resolution lives in the new `IJellyfinDirectoryResolver`.
```

Under `### Removed`, append:

```markdown
- **Backup Directory row** from the Docker Compose parse-result table on Jellyfin Settings. Path is now configured (and editable) in the Logging, Backup & Retention section.
- **Backup & Retention** and **Managed Directories** standalone sections on Jellyfin Settings — merged into the new combined **Logging, Backup & Retention** section.
```

- [ ] **Step 2: Add manual-checklist entries**

Edit `docs/manual-test-checklist.md`. Append a new section near the top (or after the existing settings section):

```markdown
## Settings Grid Redesign (2026-05-05)

### Sub-nav order

- [ ] `/settings` left rail order: General → Jellyfin → Android Devices → Cameras → Dependencies.

### General page

- [ ] General section row order: Re-run Setup Wizard, Timezone, Theme.
- [ ] Theme cell shows the hint "Also available from the icon in the top-right of every page."
- [ ] Theme buttons toggle theme immediately.
- [ ] Email (SMTP) renders as 4-row 2-col grid; per-field auto-save still works.
- [ ] Test Email button alone in the bottom row.
- [ ] ws-scrcpy-web URL field disabled in Managed mode.
- [ ] Switch to External → URL editable; blur saves with "Restart to apply" notification.

### Jellyfin page — non-migration sections

- [ ] Docker Compose parse-result table shows only Container Name + Database Path (no Backup Directory).
- [ ] Jellyfin API has a single bottom-right Save button (bi-floppy icon). Edit + Save persists all three fields (verify via reload).
- [ ] Cast & Crew Notifications has a single Save Notification Email button.

### Jellyfin page — Logging, Backup & Retention

- [ ] Section title reads "Logging, Backup & Retention".
- [ ] Three rows: Backups, Logs, Retention. Each row has its own Save button.
- [ ] Stats show file count + total size for Backups, file count for Logs.
- [ ] Backups path migration: change path to a new empty dir → existing `.db` files move; notification confirms count.
- [ ] Logs path migration: at least one log file likely locked → partial-success notification mentions the locked file by name.
- [ ] Retry-after-restart-or-rotation: re-clicking Save migrates remaining files.
- [ ] Retention save persists the new day count.
```

- [ ] **Step 3: Verify build still clean**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release --nologo && dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj -c Release --nologo`
Expected: build succeeds, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md docs/manual-test-checklist.md
git commit -m "docs: changelog + manual-checklist for settings-grid redesign"
```

---

## Task 13: Wrap-up

- [ ] **Step 1: Push branch**

Run: `git push -u origin feature/settings-grid-redesign`
Expected: branch published; remote tracking set.

- [ ] **Step 2: Update todo_control_menu.md**

Edit `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_control_menu.md`. Move Item 2 (General Settings page refinement) to the Shipped section with the merge commit reference. Keep item 1 (Homepage polish) and item 3 (Sidebar dead-clicks) as the remaining UX cluster items.

- [ ] **Step 3: Hand off**

Surface to the user that:
- Branch is `feature/settings-grid-redesign`, pushed.
- All commits land on the branch; merge to master is the user's call.
- Items 1 (Homepage) and 3 (Sidebar) are still open in the UX cluster.

---

## Self-review notes

**Spec coverage:**
- Component architecture (SettingsSection / SettingsGrid / SettingsGridCell): Tasks 2-4 ✓
- GeneralSettings rewrite: Task 5 ✓
- JellyfinSettingsSection rewrite — non-migration sections: Task 9 ✓
- JellyfinSettingsSection rewrite — Logging/Backup/Retention: Task 10 ✓
- Sub-nav reorder: Task 8 ✓
- New settings keys + OperationLogger integration: Task 6 ✓
- File migration logic: Task 6 (resolver) + Task 10 (handlers) ✓
- Save-button pattern + iconography: Tasks 9-10 ✓
- Tests (components + migration): Tasks 2-4, 6 ✓
- Manual test checklist additions: Task 12 ✓
- Branch and commits: matches spec's commit grouping pattern ✓

**Type/method consistency:**
- `IJellyfinDirectoryResolver` defined Task 6, consumed Task 7 (JellyfinService) and Task 9-10 (Razor) ✓
- `DirectoryMigrationResult` defined Task 6, consumed Task 10 ✓
- `MigrateFilesAsync(string oldDir, string newDir, string searchPattern)` signature matches across tests + impl + callers ✓
- `OperationLogger.GetDefaultBackupDirectory` / `GetDefaultLogDirectory` rename consistent across resolver + tests + caller updates ✓

**Open assumptions flagged by the spec:**
- `OperationLogger` integration: per-call resolution via injected service is the implementation choice. Static methods kept as fallback under new "Default" names.
- `bi-floppy` availability: assumed present in the project's Bootstrap Icons font. If missing, fall back to `bi-save` and update Tasks 9-10 inline.
- Migration "Retry the Save" wording: matches the spec.
