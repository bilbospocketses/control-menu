# Local-Dependencies-Only Audit Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate every system-`PATH` / "wherever-the-user-installed-it" binary invocation in Control Menu, routing every bundled dependency through a new `IDependencyPathResolver` boundary so bare-name calls become impossible at the API surface.

**Architecture:** Introduce `IDependencyPathResolver.ResolveAsync(moduleId, name) → absolute path under `dependencies/`` and add a parallel `ICommandExecutor.ExecuteResolvedAsync(moduleId, name, args, …)` overload that pipes through it. Migrate every `_executor.ExecuteAsync("adb"|"node"|"sqlite3"|"tar", …)` site to the new overload. Replace `tar` (the only external CLI we shell out to for archive handling) with `System.Formats.Tar`. Delete the existing PATH-probing / common-locations branches in `DependencyManagerService`. Document the small OS-builtin allowlist (`docker`, `powershell`, `arp`, `ping`) as the explicit, narrow exception.

**Tech Stack:** .NET 9, xUnit + Moq, Blazor Server, EF Core SQLite. Test project at `tests/ControlMenu.Tests/`.

---

## Pre-flight

- [ ] **Confirm working tree is clean or in an isolated worktree.** Plan execution touches ~30 files; if `master` has unrelated WIP, create a worktree first via `superpowers:using-git-worktrees`.
- [ ] **Confirm baseline test count.** Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --nologo --verbosity quiet` and record the passing count. Last known: ~301 passing. Every phase must end with the suite still green.

---

## File Structure

**New files:**
- `src/ControlMenu/Services/IDependencyPathResolver.cs` — interface, single async method + a `DependencyNotInstalledException`.
- `src/ControlMenu/Services/DependencyPathResolver.cs` — implementation. Reads `IToolModule.Dependencies` + `IConfigurationService` overrides + `InstallPathResolver`. No PATH lookups. No common-location probing.
- `src/ControlMenu/Services/ResolvedExecutorExtensions.cs` — extension method `ExecuteResolvedAsync(this ICommandExecutor, IDependencyPathResolver, string moduleId, string name, string? args, …)` so we don't grow the `ICommandExecutor` interface for every call site.
- `tests/ControlMenu.Tests/Services/DependencyPathResolverTests.cs` — unit tests for the resolver.
- `tests/ControlMenu.Tests/Services/ResolvedExecutorExtensionsTests.cs` — tests for the extension method.

**Modified files (consumer migration):**
- `src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs` — ~25 call sites, all `("adb", …)` → `("android-devices", "adb", …)`.
- `src/ControlMenu/Services/DependencyManagerService.cs` — line 329 (`adb kill-server`); line 304 (`tar`) replaced with `System.Formats.Tar`; remove `TryScanPathAsync`, `GetCommonLocations`, and PATH-fallback branch in `GetInstalledVersionAsync`.
- `src/ControlMenu/Services/WsScrcpyService.cs` — line 98 (`FileName = "node"`) resolved to `dependencies/node/node.exe` via the new resolver.
- `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs` — line 117 (`sqlite3`) routed through resolver.
- `src/ControlMenu/Program.cs` — DI registration of `IDependencyPathResolver`.
- `src/ControlMenu/Modules/Jellyfin/JellyfinModule.cs` — confirm `sqlite3` dep already declares correct `InstallPath` (it does, per audit).

**Modified test files:**
- `tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs` — every `Mock<ICommandExecutor>.Setup(e => e.ExecuteAsync("adb", …))` becomes a setup against the new extension/resolver. Strategy: have the fake resolver return `"adb"` so the existing string-based assertions still match. (Detail in Task 5.)
- `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs` — remove tests for PATH-fallback paths; update tests for `tar` archive extraction to expect `System.Formats.Tar`.
- `tests/ControlMenu.Tests/Services/DependencyScanTests.cs` — remove PATH/common-location scan tests (or assert they no longer find anything).
- `tests/ControlMenu.Tests/Services/WsScrcpyServiceTests.cs` — update node path expectations.
- `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs` — update sqlite3 path expectations.

**Documentation updates:**
- `src/ControlMenu/Services/CommandExecutor.cs` — XML doc on the raw `ExecuteAsync(string command, …)` overload warning that it's only for the OS-builtin allowlist.
- `project_control_menu.md` (memory) — add allowlist policy.
- `CHANGELOG.md` — entry under Unreleased.
- `TECHNICAL_GUIDE.md` (if present) — update the dependency-management section.

---

## Phase 0 — Contract: resolver service (serial; blocks everything else)

### Task 0.1: Define `DependencyNotInstalledException`

**Files:**
- Create: `src/ControlMenu/Services/IDependencyPathResolver.cs` (will hold both the interface and the exception)

- [ ] **Step 1: Write the failing test**

Create `tests/ControlMenu.Tests/Services/DependencyPathResolverTests.cs`:

```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class DependencyPathResolverTests
{
    [Fact]
    public void DependencyNotInstalledException_CarriesModuleAndName()
    {
        var ex = new DependencyNotInstalledException("android-devices", "adb", "/expected/path/adb.exe");

        Assert.Equal("android-devices", ex.ModuleId);
        Assert.Equal("adb", ex.Name);
        Assert.Equal("/expected/path/adb.exe", ex.ExpectedPath);
        Assert.Contains("adb", ex.Message);
        Assert.Contains("android-devices", ex.Message);
    }
}
```

- [ ] **Step 2: Run the test, verify it fails**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter DependencyNotInstalledException_CarriesModuleAndName`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement**

Write `src/ControlMenu/Services/IDependencyPathResolver.cs`:

```csharp
namespace ControlMenu.Services;

public interface IDependencyPathResolver
{
    /// <summary>
    /// Returns the absolute path to a bundled binary, applying any user-configured override.
    /// Throws <see cref="DependencyNotInstalledException"/> if the binary is not present locally.
    /// Per the project's "Local Dependencies Only" rule, this is the ONLY supported way to obtain
    /// a path to a bundled binary — never use system PATH, env vars, or "wherever the user installed it".
    /// </summary>
    Task<string> ResolveAsync(string moduleId, string name, CancellationToken cancellationToken = default);
}

public sealed class DependencyNotInstalledException : Exception
{
    public string ModuleId { get; }
    public string Name { get; }
    public string ExpectedPath { get; }

    public DependencyNotInstalledException(string moduleId, string name, string expectedPath)
        : base($"Dependency '{name}' (module '{moduleId}') is not installed locally. Expected at: {expectedPath}")
    {
        ModuleId = moduleId;
        Name = name;
        ExpectedPath = expectedPath;
    }
}
```

- [ ] **Step 4: Verify**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter DependencyNotInstalledException_CarriesModuleAndName`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/IDependencyPathResolver.cs tests/ControlMenu.Tests/Services/DependencyPathResolverTests.cs
git commit -m "feat(deps): add IDependencyPathResolver contract + exception"
```

---

### Task 0.2: Implement `DependencyPathResolver` — happy path

**Files:**
- Create: `src/ControlMenu/Services/DependencyPathResolver.cs`
- Modify: `tests/ControlMenu.Tests/Services/DependencyPathResolverTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `DependencyPathResolverTests.cs`:

```csharp
using ControlMenu.Modules;
using ControlMenu.Data.Enums;
using Moq;

public class DependencyPathResolverResolveTests
{
    private static IToolModule MakeModule(string id, params ModuleDependency[] deps)
    {
        var mock = new Mock<IToolModule>();
        mock.Setup(m => m.Id).Returns(id);
        mock.Setup(m => m.Dependencies).Returns(deps);
        return mock.Object;
    }

    [Fact]
    public async Task ResolveAsync_ReturnsLocalExePath_WhenFileExists()
    {
        var tempDir = Directory.CreateTempSubdirectory("cm-resolver-test");
        try
        {
            var exePath = Path.Combine(tempDir.FullName, "adb.exe");
            File.WriteAllText(exePath, "fake-binary");

            var dep = new ModuleDependency
            {
                Name = "adb",
                ExecutableName = "adb",
                VersionCommand = "adb --version",
                VersionPattern = @"([\d.]+)",
                InstallPath = tempDir.FullName
            };
            var module = MakeModule("android-devices", dep);

            var config = new Mock<IConfigurationService>();
            config.Setup(c => c.GetSettingAsync("dep-path-adb", It.IsAny<string?>()))
                  .ReturnsAsync((string?)null);

            var resolver = new DependencyPathResolver(new[] { module }, config.Object);

            var result = await resolver.ResolveAsync("android-devices", "adb");

            Assert.Equal(exePath, result, ignoreCase: true);
        }
        finally { tempDir.Delete(recursive: true); }
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter ResolveAsync_ReturnsLocalExePath_WhenFileExists`
Expected: FAIL — `DependencyPathResolver` does not exist.

- [ ] **Step 3: Implement**

Write `src/ControlMenu/Services/DependencyPathResolver.cs`:

```csharp
using ControlMenu.Modules;

namespace ControlMenu.Services;

public class DependencyPathResolver : IDependencyPathResolver
{
    private readonly IReadOnlyList<IToolModule> _modules;
    private readonly IConfigurationService _config;

    public DependencyPathResolver(IEnumerable<IToolModule> modules, IConfigurationService config)
    {
        _modules = modules.ToList();
        _config = config;
    }

    public async Task<string> ResolveAsync(string moduleId, string name, CancellationToken cancellationToken = default)
    {
        var module = _modules.FirstOrDefault(m => m.Id == moduleId)
            ?? throw new DependencyNotInstalledException(moduleId, name,
                $"<unknown module '{moduleId}'>");

        var dep = module.Dependencies.FirstOrDefault(d => d.Name == name)
            ?? throw new DependencyNotInstalledException(moduleId, name,
                $"<dependency '{name}' not declared in module '{moduleId}'>");

        if (dep.InstallPath is null)
            throw new DependencyNotInstalledException(moduleId, name,
                $"<dependency '{name}' has no InstallPath; cannot be a local binary>");

        var customPath = await _config.GetSettingAsync($"dep-path-{name}");
        var installDir = InstallPathResolver.Resolve(dep.InstallPath, customPath);

        var exeName = dep.ExecutableName;
        if (OperatingSystem.IsWindows() && !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exeName += ".exe";

        var exePath = Path.Combine(installDir, exeName);
        if (!File.Exists(exePath))
            throw new DependencyNotInstalledException(moduleId, name, exePath);

        return exePath;
    }
}
```

- [ ] **Step 4: Verify**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter ResolveAsync_ReturnsLocalExePath_WhenFileExists`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Services/DependencyPathResolver.cs tests/ControlMenu.Tests/Services/DependencyPathResolverTests.cs
git commit -m "feat(deps): implement DependencyPathResolver happy path"
```

---

### Task 0.3: Resolver — missing-binary, unknown-module, unknown-dep, override paths

**Files:**
- Modify: `tests/ControlMenu.Tests/Services/DependencyPathResolverTests.cs`

- [ ] **Step 1: Write failing tests**

Append four tests to the same file:

```csharp
[Fact]
public async Task ResolveAsync_Throws_WhenBinaryMissing()
{
    var tempDir = Directory.CreateTempSubdirectory("cm-resolver-missing");
    try
    {
        var dep = new ModuleDependency
        {
            Name = "adb", ExecutableName = "adb",
            VersionCommand = "adb --version", VersionPattern = @"([\d.]+)",
            InstallPath = tempDir.FullName
        };
        var module = MakeModule("android-devices", dep);
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync((string?)null);
        var resolver = new DependencyPathResolver(new[] { module }, config.Object);

        var ex = await Assert.ThrowsAsync<DependencyNotInstalledException>(
            () => resolver.ResolveAsync("android-devices", "adb"));
        Assert.Equal("adb", ex.Name);
        Assert.Contains(tempDir.FullName, ex.ExpectedPath);
    }
    finally { tempDir.Delete(recursive: true); }
}

[Fact]
public async Task ResolveAsync_Throws_WhenModuleUnknown()
{
    var resolver = new DependencyPathResolver(Array.Empty<IToolModule>(), new Mock<IConfigurationService>().Object);
    await Assert.ThrowsAsync<DependencyNotInstalledException>(
        () => resolver.ResolveAsync("nope", "adb"));
}

[Fact]
public async Task ResolveAsync_Throws_WhenDependencyNotDeclared()
{
    var module = MakeModule("android-devices");
    var resolver = new DependencyPathResolver(new[] { module }, new Mock<IConfigurationService>().Object);
    await Assert.ThrowsAsync<DependencyNotInstalledException>(
        () => resolver.ResolveAsync("android-devices", "adb"));
}

[Fact]
public async Task ResolveAsync_HonorsUserOverride()
{
    var defaultDir = Directory.CreateTempSubdirectory("cm-resolver-default");
    var overrideDir = Directory.CreateTempSubdirectory("cm-resolver-override");
    try
    {
        var exeInOverride = Path.Combine(overrideDir.FullName, "adb.exe");
        File.WriteAllText(exeInOverride, "fake");

        var dep = new ModuleDependency
        {
            Name = "adb", ExecutableName = "adb",
            VersionCommand = "adb --version", VersionPattern = @"([\d.]+)",
            InstallPath = defaultDir.FullName
        };
        var module = MakeModule("android-devices", dep);
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetSettingAsync("dep-path-adb", It.IsAny<string?>()))
              .ReturnsAsync(overrideDir.FullName);
        var resolver = new DependencyPathResolver(new[] { module }, config.Object);

        var result = await resolver.ResolveAsync("android-devices", "adb");
        Assert.Equal(exeInOverride, result, ignoreCase: true);
    }
    finally { defaultDir.Delete(recursive: true); overrideDir.Delete(recursive: true); }
}
```

- [ ] **Step 2: Verify all four pass already** (the implementation from Task 0.2 should cover them)

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter DependencyPathResolverResolveTests`
Expected: 5 PASS (1 from 0.2 + 4 new).

- [ ] **Step 3: Commit**

```bash
git add tests/ControlMenu.Tests/Services/DependencyPathResolverTests.cs
git commit -m "test(deps): cover resolver edge cases (missing/unknown/override)"
```

---

### Task 0.4: Register `IDependencyPathResolver` in DI

**Files:**
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Locate the DI registration block**

Open `src/ControlMenu/Program.cs`, find where other singleton services like `ICommandExecutor` and `IConfigurationService` are registered.

- [ ] **Step 2: Add the registration**

Add (alongside other service registrations):

```csharp
builder.Services.AddSingleton<IDependencyPathResolver>(sp =>
    new DependencyPathResolver(
        sp.GetRequiredService<IReadOnlyList<IToolModule>>(),
        sp.GetRequiredService<IConfigurationService>()));
```

If `IReadOnlyList<IToolModule>` isn't already in DI, use `sp.GetServices<IToolModule>()` instead.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj --nologo`
Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --nologo`
Expected: baseline + new resolver tests, all green.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Program.cs
git commit -m "feat(deps): register IDependencyPathResolver in DI"
```

---

## Phase 1 — Executor extension (serial; blocks consumer migration)

### Task 1.1: Add `ExecuteResolvedAsync` extension method

**Files:**
- Create: `src/ControlMenu/Services/ResolvedExecutorExtensions.cs`
- Create: `tests/ControlMenu.Tests/Services/ResolvedExecutorExtensionsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ControlMenu.Tests/Services/ResolvedExecutorExtensionsTests.cs`:

```csharp
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Services;

public class ResolvedExecutorExtensionsTests
{
    [Fact]
    public async Task ExecuteResolvedAsync_PassesResolvedPathToExecutor()
    {
        var executor = new Mock<ICommandExecutor>();
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                .ReturnsAsync("C:/cm/dependencies/platform-tools/adb.exe");
        executor.Setup(e => e.ExecuteAsync("C:/cm/dependencies/platform-tools/adb.exe",
                                            "devices", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CommandResult(0, "List of devices attached", "", false));

        var result = await executor.Object.ExecuteResolvedAsync(
            resolver.Object, "android-devices", "adb", "devices");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("List of devices", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteResolvedAsync_PropagatesNotInstalledException()
    {
        var executor = new Mock<ICommandExecutor>();
        var resolver = new Mock<IDependencyPathResolver>();
        resolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DependencyNotInstalledException("android-devices", "adb", "/missing"));

        await Assert.ThrowsAsync<DependencyNotInstalledException>(() =>
            executor.Object.ExecuteResolvedAsync(resolver.Object, "android-devices", "adb", "devices"));
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter ResolvedExecutorExtensionsTests`
Expected: FAIL — extension doesn't exist.

- [ ] **Step 3: Implement**

Create `src/ControlMenu/Services/ResolvedExecutorExtensions.cs`:

```csharp
namespace ControlMenu.Services;

public static class ResolvedExecutorExtensions
{
    /// <summary>
    /// Executes a bundled local binary identified by (moduleId, name). The path is resolved through
    /// <see cref="IDependencyPathResolver"/> — the ONLY supported way to invoke a bundled binary in
    /// this codebase. Throws <see cref="DependencyNotInstalledException"/> if the binary isn't installed.
    /// </summary>
    /// <remarks>
    /// Do NOT add OS-builtin allowlist entries here (docker, powershell, arp, ping). Those go through
    /// the raw <see cref="ICommandExecutor.ExecuteAsync(string, string?, string?, CancellationToken)"/>
    /// overload, which is reserved for the documented allowlist only.
    /// </remarks>
    public static async Task<CommandResult> ExecuteResolvedAsync(
        this ICommandExecutor executor,
        IDependencyPathResolver resolver,
        string moduleId,
        string name,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var path = await resolver.ResolveAsync(moduleId, name, cancellationToken);
        return await executor.ExecuteAsync(path, arguments, workingDirectory, cancellationToken);
    }
}
```

- [ ] **Step 4: Verify**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter ResolvedExecutorExtensionsTests`
Expected: 2 PASS.

- [ ] **Step 5: Add XML doc to raw `ExecuteAsync` overload**

Modify `src/ControlMenu/Services/ICommandExecutor.cs`. Replace the interface body with:

```csharp
namespace ControlMenu.Services;

public interface ICommandExecutor
{
    /// <summary>
    /// Executes a command by raw path or PATH-resolvable name. RESERVED for the OS-builtin allowlist
    /// only: <c>docker</c>, <c>powershell</c>, <c>arp</c>, <c>ping</c>. For bundled binaries
    /// (adb, scrcpy, node, sqlite3, go2rtc, ws-scrcpy-web) use
    /// <see cref="ResolvedExecutorExtensions.ExecuteResolvedAsync"/> instead.
    /// </summary>
    Task<CommandResult> ExecuteAsync(
        string command,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task<CommandResult> ExecuteAsync(
        CommandDefinition definition,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Verify build + full suite**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj --nologo && dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --nologo`
Expected: green.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Services/ResolvedExecutorExtensions.cs src/ControlMenu/Services/ICommandExecutor.cs tests/ControlMenu.Tests/Services/ResolvedExecutorExtensionsTests.cs
git commit -m "feat(deps): add ExecuteResolvedAsync extension + reserve raw overload for OS-builtin allowlist"
```

---

## Phase 2 — Migrate `AdbService` (parallelizable with Phases 3 & 4 after Phase 1)

### Task 2.1: Update `AdbServiceTests` to expect resolved-path executor

**Files:**
- Modify: `tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs`

- [ ] **Step 1: Update test setup to inject resolver fake**

Open `tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs`. Replace the field initialization and `CreateService` helper at the top (currently lines 7-11):

```csharp
private readonly Mock<ICommandExecutor> _mockExecutor = new();
private readonly Mock<IDependencyPathResolver> _mockResolver = new();

public AdbServiceTests()
{
    // Resolver returns the literal string "adb" so existing string-based Setup() calls keep matching.
    _mockResolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("adb");
}

private AdbService CreateService() => new(_mockExecutor.Object, _mockResolver.Object);
```

This keeps every existing `_mockExecutor.Setup(e => e.ExecuteAsync("adb", "<args>", null, default))` valid — the extension method calls `executor.ExecuteAsync(<resolved-path>, args, ...)`, and the resolver returns `"adb"`, so the existing string match holds.

- [ ] **Step 2: Run tests — they will fail to compile** (`AdbService` constructor mismatch)

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter AdbServiceTests`
Expected: BUILD FAIL — `AdbService` doesn't yet take a resolver. That's expected; the next task fixes it.

- [ ] **Step 3: Commit (red state)**

```bash
git add tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs
git commit -m "test(adb): inject IDependencyPathResolver fake into AdbServiceTests"
```

---

### Task 2.2: Migrate `AdbService` constructor + every call site

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs`

- [ ] **Step 1: Update constructor to take resolver**

Replace the top of `AdbService.cs` (lines 6-13):

```csharp
public partial class AdbService : IAdbService
{
    private readonly ICommandExecutor _executor;
    private readonly IDependencyPathResolver _resolver;

    public AdbService(ICommandExecutor executor, IDependencyPathResolver resolver)
    {
        _executor = executor;
        _resolver = resolver;
    }

    private string DeviceArg(string ip, int port) => $"-s {ip}:{port}";

    private Task<CommandResult> AdbAsync(string args, CancellationToken ct = default) =>
        _executor.ExecuteResolvedAsync(_resolver, "android-devices", "adb", args, null, ct);
```

The new private `AdbAsync` helper collapses every call site to one line.

- [ ] **Step 2: Replace all 25 call sites**

Every line currently shaped like `await _executor.ExecuteAsync("adb", $"<args>", null, ct)` becomes `await AdbAsync($"<args>", ct)`. For lines that capture the result, `var result = await AdbAsync(...)`.

Concrete replacements (verify each line in the diff matches the corresponding source line):

| Source line | Old | New |
|---|---|---|
| 19 | `var result = await _executor.ExecuteAsync("adb", $"connect {ip}:{port}", null, ct);` | `var result = await AdbAsync($"connect {ip}:{port}", ct);` |
| 25 | `await _executor.ExecuteAsync("adb", $"disconnect {ip}:{port}", null, ct);` | `await AdbAsync($"disconnect {ip}:{port}", ct);` |
| 30 | `var result = await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell dumpsys power", null, ct);` | `var result = await AdbAsync($"{DeviceArg(ip, port)} shell dumpsys power", ct);` |
| 40 | `await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell reboot", null, ct);` | `await AdbAsync($"{DeviceArg(ip, port)} shell reboot", ct);` |
| 45 | `await _executor.ExecuteAsync("adb", $"{DeviceArg(ip, port)} shell input keyevent KEYCODE_POWER", null, ct);` | `await AdbAsync($"{DeviceArg(ip, port)} shell input keyevent KEYCODE_POWER", ct);` |
| 50, 73, 84, 109, 126, 147, 168, 241 | `var result = await _executor.ExecuteAsync("adb", "<args>", null, ct);` | `var result = await AdbAsync("<args>", ct);` |
| 68, 79, 92, 93, 97, 98, 104, 119, 181, 182, 183, 184, 192 | `await _executor.ExecuteAsync("adb", "<args>", null, ct);` | `await AdbAsync("<args>", ct);` |

Do this with a single `Edit` per logical block, or `replace_all` for the verbatim pattern `await _executor.ExecuteAsync("adb", `. Verify the file compiles after.

- [ ] **Step 3: Add `using ControlMenu.Services;` if not already present**

The top of `AdbService.cs` already has `using ControlMenu.Services;` so the extension method is in scope.

- [ ] **Step 4: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj --nologo`
Expected: build succeeds.

- [ ] **Step 5: Verify all AdbService tests still pass**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter AdbServiceTests --nologo`
Expected: every existing AdbService test green (because the resolver fake returns `"adb"` and the executor mock matches the same string).

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/AndroidDevices/Services/AdbService.cs
git commit -m "refactor(adb): route every adb call through IDependencyPathResolver"
```

---

### Task 2.3: Add a regression test that proves `AdbService` uses the resolver

**Files:**
- Modify: `tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs`

- [ ] **Step 1: Append a regression test**

Add to `AdbServiceTests.cs`:

```csharp
[Fact]
public async Task AdbService_ResolvesViaDependencyPathResolver_NotBareName()
{
    // Have the resolver return a clearly-not-PATH path
    _mockResolver.Setup(r => r.ResolveAsync("android-devices", "adb", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("/cm/local/adb.exe");
    _mockExecutor.Setup(e => e.ExecuteAsync("/cm/local/adb.exe", "devices", null, default))
                 .ReturnsAsync(new CommandResult(0, "List of devices attached", "", false));

    var service = new AdbService(_mockExecutor.Object, _mockResolver.Object);
    await service.GetConnectedDevicesAsync();

    _mockExecutor.Verify(e => e.ExecuteAsync("adb", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never, "AdbService must NOT call the executor with bare 'adb' — local-deps rule.");
    _mockExecutor.Verify(e => e.ExecuteAsync("/cm/local/adb.exe", "devices", null, default), Times.Once);
}
```

(If `GetConnectedDevicesAsync` isn't the public method name, substitute the actual method that runs `adb devices` — see `AdbService.cs:126`.)

- [ ] **Step 2: Run, verify it passes**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter AdbService_ResolvesViaDependencyPathResolver`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/ControlMenu.Tests/Modules/AndroidDevices/AdbServiceTests.cs
git commit -m "test(adb): regression for resolver-only path resolution"
```

---

## Phase 3 — Migrate other consumers (parallelizable; one task per consumer)

### Task 3.1: `WsScrcpyService` — node binary

**Files:**
- Modify: `src/ControlMenu/Services/WsScrcpyService.cs`
- Modify: `tests/ControlMenu.Tests/Services/WsScrcpyServiceTests.cs`

- [ ] **Step 1: Inspect current spawn site**

Open `src/ControlMenu/Services/WsScrcpyService.cs:89-108` (`SpawnProcess()`). Currently `FileName = "node"`.

- [ ] **Step 2: Write the failing test**

In `WsScrcpyServiceTests.cs`, add:

```csharp
[Fact]
public async Task SpawnProcess_UsesResolvedNodePath_NotBareName()
{
    var resolver = new Mock<IDependencyPathResolver>();
    resolver.Setup(r => r.ResolveAsync("android-devices", "node", It.IsAny<CancellationToken>()))
            .ReturnsAsync("/cm/local/node.exe");
    // ... wire resolver into the service constructor (depends on existing test scaffolding)
    // Assert that a captured ProcessStartInfo.FileName equals "/cm/local/node.exe"
}
```

(Detailed scaffolding depends on `WsScrcpyServiceTests.cs` existing structure — read first, then write the matching shape.)

- [ ] **Step 3: Update `WsScrcpyService` constructor**

Add `IDependencyPathResolver _resolver` field, accept it in the constructor.

- [ ] **Step 4: Update `SpawnProcess` to resolve node**

Since `SpawnProcess` is invoked from sync code under `_lock`, refactor to resolve the path *before* taking the lock (or make the spawn flow async). Easiest: resolve once at startup (during the existing `EnsureStartedAsync` flow) and cache `_nodePath`. Then `FileName = _nodePath`.

Sketch:

```csharp
private string? _nodePath;

private async Task EnsureNodePathAsync(CancellationToken ct)
{
    _nodePath ??= await _resolver.ResolveAsync("android-devices", "node", ct);
}

// In SpawnProcess (under lock), use _nodePath (must be non-null by this point):
FileName = _nodePath ?? throw new InvalidOperationException("Node path not resolved before SpawnProcess"),
```

Call `EnsureNodePathAsync` from whatever async entry point currently triggers `SpawnProcess` (likely `StartAsync` / `Restart`).

- [ ] **Step 5: Update DI / callers if signature changed**

If `Program.cs` constructs `WsScrcpyService` directly, add the resolver argument. If via DI, the new param resolves automatically.

- [ ] **Step 6: Verify build + tests**

Run: `dotnet build && dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter WsScrcpyServiceTests --nologo`
Expected: green.

- [ ] **Step 7: Commit**

```bash
git add src/ControlMenu/Services/WsScrcpyService.cs tests/ControlMenu.Tests/Services/WsScrcpyServiceTests.cs
git commit -m "refactor(ws-scrcpy): resolve node path via IDependencyPathResolver"
```

---

### Task 3.2: `JellyfinService` — sqlite3 binary

**Files:**
- Modify: `src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs`
- Modify: `tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs`

- [ ] **Step 1: Inject resolver into `JellyfinService`**

Add `IDependencyPathResolver _resolver` to the constructor (alongside existing dependencies).

- [ ] **Step 2: Update line 117**

Replace:
```csharp
var result = await _executor.ExecuteAsync("sqlite3",
    $"\"{dbPath}\" \"UPDATE BaseItems SET DateCreated=PremiereDate WHERE PremiereDate IS NOT NULL;\"",
    null, ct);
```

With:
```csharp
var result = await _executor.ExecuteResolvedAsync(_resolver, "jellyfin", "sqlite3",
    $"\"{dbPath}\" \"UPDATE BaseItems SET DateCreated=PremiereDate WHERE PremiereDate IS NOT NULL;\"",
    null, ct);
```

- [ ] **Step 3: Add a regression test**

In `JellyfinServiceTests.cs`:

```csharp
[Fact]
public async Task SqliteUpdate_ResolvesViaDependencyPathResolver()
{
    var resolver = new Mock<IDependencyPathResolver>();
    resolver.Setup(r => r.ResolveAsync("jellyfin", "sqlite3", It.IsAny<CancellationToken>()))
            .ReturnsAsync("/cm/local/sqlite3.exe");
    var executor = new Mock<ICommandExecutor>();
    executor.Setup(e => e.ExecuteAsync("/cm/local/sqlite3.exe", It.IsAny<string>(), null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));
    // ... rest of scaffolding matching existing JellyfinServiceTests style
}
```

- [ ] **Step 4: Verify build + tests**

Run: `dotnet build && dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter JellyfinServiceTests --nologo`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/Jellyfin/Services/JellyfinService.cs tests/ControlMenu.Tests/Modules/Jellyfin/JellyfinServiceTests.cs
git commit -m "refactor(jellyfin): resolve sqlite3 path via IDependencyPathResolver"
```

---

### Task 3.3: `DependencyManagerService` — `adb kill-server` line 329

**Files:**
- Modify: `src/ControlMenu/Services/DependencyManagerService.cs`

- [ ] **Step 1: Inject resolver**

Add `IDependencyPathResolver _resolver` to the `DependencyManagerService` constructor (after `IGo2RtcService _go2Rtc`).

**Note:** `DependencyManagerService` itself can NOT *use* the resolver to look up its own version-checks blindly because the resolver throws when a binary isn't installed yet. The dep manager needs to handle "not yet installed" gracefully. See Task 4.1 for that refactor — Task 3.3 only fixes the one `kill-server` line that runs *after* installation.

- [ ] **Step 2: Replace line 329**

Change:
```csharp
await _executor.ExecuteAsync("adb", "kill-server");
```

To:
```csharp
try
{
    await _executor.ExecuteResolvedAsync(_resolver, "android-devices", "adb", "kill-server");
}
catch (DependencyNotInstalledException)
{
    // adb not installed — nothing to kill. Continue with the install/swap.
}
```

- [ ] **Step 3: Verify build + tests**

Run: `dotnet build && dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter DependencyManagerServiceTests --nologo`
Expected: green.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Services/DependencyManagerService.cs
git commit -m "refactor(deps): kill-server uses resolved adb path; tolerate not-yet-installed"
```

---

### Task 3.4: Replace external `tar` with `System.Formats.Tar`

**Files:**
- Modify: `src/ControlMenu/Services/DependencyManagerService.cs`
- Modify: `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs` (if extraction is tested)

- [ ] **Step 1: Locate extraction block**

`DependencyManagerService.cs:296-307` — currently:

```csharp
var extractDir = Path.Combine(tempDir, "extracted");
if (tempFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
{
    System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, extractDir);
}
else if (tempFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
{
    var result = await _executor.ExecuteAsync("tar", $"xzf \"{tempFile}\" -C \"{extractDir}\"");
    if (result.ExitCode != 0)
        return new UpdateResult(false, null, $"Extraction failed: {result.StandardError}", urlAction);
}
```

- [ ] **Step 2: Replace tar branch with framework API**

```csharp
else if (tempFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
{
    Directory.CreateDirectory(extractDir);
    await using var fileStream = File.OpenRead(tempFile);
    await using var gzipStream = new System.IO.Compression.GZipStream(
        fileStream, System.IO.Compression.CompressionMode.Decompress);
    await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(
        gzipStream, extractDir, overwriteFiles: true);
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj --nologo`
Expected: green. `System.Formats.Tar` is built into .NET 7+ and available in .NET 9 without a NuGet reference.

- [ ] **Step 4: Add a unit test for the tar.gz path**

In `DependencyManagerServiceTests.cs`, add a test that creates a small in-memory tar.gz, hands it to the extraction logic (factor out a private helper if needed), and asserts the extracted file appears at the expected path. If the extraction is too tightly coupled to `DownloadAndInstallAsync` to test in isolation, leave the assertion at "build succeeds" + add a manual smoke test note in the verification section. Do NOT skip the test if extraction is reachable.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --nologo`
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Services/DependencyManagerService.cs tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs
git commit -m "refactor(deps): replace external tar with System.Formats.Tar"
```

---

## Phase 4 — Strip PATH/common-location probing from `DependencyManagerService`

### Task 4.1: Remove `TryScanPathAsync` + `GetCommonLocations`

**Files:**
- Modify: `src/ControlMenu/Services/DependencyManagerService.cs`
- Modify: `tests/ControlMenu.Tests/Services/DependencyScanTests.cs`
- Modify: `tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs`

- [ ] **Step 1: Update tests first (red state)**

Open `tests/ControlMenu.Tests/Services/DependencyScanTests.cs`. Identify tests that:
- Assert `Source == "PATH"` is returned (these will fail after the refactor — delete them).
- Assert common-location probing finds binaries at `C:\platform-tools\` etc. (delete).
- Keep tests that verify "Previously configured" lookup and "Not found" fallback.

Add a new test asserting that PATH-only deps are reported as "Not found":

```csharp
[Fact]
public async Task ScanForDependenciesAsync_DoesNotProbePath()
{
    // With NO local install present and no DB record, scan must report Not Found,
    // not "Source = PATH" (which would be a CLAUDE.md violation).
    // ...arrange a module dep whose binary is NOT under dependencies/...
    var results = await service.ScanForDependenciesAsync();
    Assert.All(results, r => Assert.NotEqual("PATH", r.Source));
}
```

Then in `DependencyManagerServiceTests.cs`, find any test verifying the PATH fallback in `GetInstalledVersionAsync` and delete or invert it.

- [ ] **Step 2: Run tests, verify the deleted-fallback ones pass and the kept ones still work**

Run: `dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --filter Dependency --nologo`
Expected: tests adjusted — count drops by however many PATH-fallback tests we removed.

- [ ] **Step 3: Delete `TryScanPathAsync` + `GetCommonLocations`**

In `DependencyManagerService.cs`:
- Delete the `TryScanPathAsync` method (currently lines 487-510).
- Delete the `TryScanCommonLocationsAsync` method (currently lines 512-545).
- Delete the `GetCommonLocations` method (currently lines 547-564).
- In `ScanForDependenciesAsync` (line 393), remove the calls to `TryScanPathAsync` and `TryScanCommonLocationsAsync`. The flow becomes: check DB → if not configured, report "Not found".

The trimmed `ScanForDependenciesAsync` body looks like:

```csharp
foreach (var module in _modules)
{
    foreach (var dep in module.Dependencies)
    {
        var entity = existing.FirstOrDefault(e =>
            e.ModuleId == module.Id && e.Name == dep.Name);

        if (entity?.InstalledVersion is not null)
        {
            results.Add(new DependencyScanResult(
                dep.Name, module.Id, Found: true,
                Path: null, Version: entity.InstalledVersion,
                Source: "Previously configured"));
            continue;
        }

        results.Add(new DependencyScanResult(
            dep.Name, module.Id, Found: false,
            Path: null, Version: null,
            Source: "Not found"));
    }
}
```

- [ ] **Step 4: Strip PATH fallback in `GetInstalledVersionAsync`**

Currently `GetInstalledVersionAsync` (lines 566-613) has two branches: local-only when `dep.InstallPath` is set, and "use system PATH" otherwise. Delete the second branch entirely. New shape:

```csharp
private async Task<string?> GetInstalledVersionAsync(ModuleDependency dep, string? moduleId = null)
{
    if (moduleId is null || dep.InstallPath is null)
        return null; // No local install path declared — we can't (and won't) check PATH.

    var parts = dep.VersionCommand.Split(' ', 2);
    var args = parts.Length > 1 ? parts[1] : null;

    var customPath = await _config.GetSettingAsync($"dep-path-{dep.Name}");
    var installDir = InstallPathResolver.Resolve(dep.InstallPath, customPath);

    var exeName = dep.ExecutableName;
    if (OperatingSystem.IsWindows() && !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        exeName += ".exe";

    var localExe = Path.Combine(installDir, exeName);
    if (!File.Exists(localExe))
        return null;

    try
    {
        var localResult = await _executor.ExecuteAsync(localExe, args);
        if (localResult.ExitCode == 0)
            return ExtractVersion(localResult.StandardOutput, dep.VersionPattern);
    }
    catch { /* binary exists but failed to run */ }

    return null;
}
```

- [ ] **Step 5: Build + full test suite**

Run: `dotnet build && dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --nologo`
Expected: all tests green; total count reduced by however many PATH-fallback tests we removed.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Services/DependencyManagerService.cs tests/ControlMenu.Tests/Services/DependencyScanTests.cs tests/ControlMenu.Tests/Services/DependencyManagerServiceTests.cs
git commit -m "refactor(deps): remove system-PATH and common-location probing"
```

---

## Phase 5 — Document the OS-builtin allowlist policy

### Task 5.1: Document allowlist in code

**Files:**
- Modify: `src/ControlMenu/Services/CommandExecutor.cs`

- [ ] **Step 1: Add an allowlist comment**

Above the class declaration, add:

```csharp
/// <summary>
/// Executes external commands. By project policy ("Local Dependencies Only" — see CLAUDE.md), the raw
/// (string command, …) overload is reserved for OS-builtin / genuinely external commands ONLY:
/// <list type="bullet">
///   <item><c>docker</c> — external daemon, not vendorable</item>
///   <item><c>powershell</c> — OS-shipped on Windows</item>
///   <item><c>arp</c> — OS-shipped network utility</item>
///   <item><c>ping</c> — OS-shipped network utility</item>
/// </list>
/// All bundled binaries (adb, scrcpy, node, sqlite3, go2rtc, ws-scrcpy-web) MUST go through
/// <see cref="ResolvedExecutorExtensions.ExecuteResolvedAsync"/>.
/// </summary>
```

- [ ] **Step 2: Commit**

```bash
git add src/ControlMenu/Services/CommandExecutor.cs
git commit -m "docs(deps): document OS-builtin allowlist policy on CommandExecutor"
```

---

### Task 5.2: Update `CHANGELOG.md`

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add an entry under `## [Unreleased]`**

```markdown
### Changed
- **Local-Dependencies-Only audit (architectural):** All bundled binaries (adb, node, sqlite3, scrcpy, go2rtc) are now resolved through a new `IDependencyPathResolver` boundary; bare-name calls that previously fell through to system `PATH` have been eliminated. Removed `DependencyManagerService`'s system-PATH probing and common-location heuristics. The raw `ICommandExecutor` overload is now reserved for the documented OS-builtin allowlist (`docker`, `powershell`, `arp`, `ping`). Replaces external `tar` with `System.Formats.Tar`.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: changelog entry for local-deps audit fix"
```

---

## Phase 6 — Verification

### Task 6.1: Static audit — no bare-name violations remain

- [ ] **Step 1: Grep for bare-name executor calls**

Run:
```bash
grep -rn "_executor\.ExecuteAsync(\"" src/ControlMenu/ | \
  grep -v "ExecuteAsync(\"docker\"" | \
  grep -v "ExecuteAsync(\"powershell\"" | \
  grep -v "ExecuteAsync(\"arp\"" | \
  grep -v "ExecuteAsync(\"ping\""
```

Expected: zero matches. If any remain, they're either undocumented OS-builtins (decide: vendor or allowlist) or missed migrations (fix and recommit).

- [ ] **Step 2: Grep for `Process.Start` / `FileName =` outside CommandExecutor**

Run:
```bash
grep -rn "FileName = \"" src/ControlMenu/
```

Expected: only `Go2RtcService.cs` (resolves via `FindDepsRoot`) and `WsScrcpyService.cs` (now uses `_nodePath` which came from the resolver). Any literal-string `FileName` for a bundled binary is a bug.

---

### Task 6.2: Full test suite + build

- [ ] **Step 1: Clean build + test**

Run:
```bash
dotnet build src/ControlMenu/ControlMenu.csproj --nologo
dotnet test tests/ControlMenu.Tests/ControlMenu.Tests.csproj --nologo
```

Expected: 0 build errors; passing test count = baseline + new resolver tests + new regression tests − removed PATH-fallback tests. Document the delta in the commit message of Task 6.4.

---

### Task 6.3: Manual smoke

- [ ] **Step 1: Start the app**

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj`
Open: `http://localhost:5159`

- [ ] **Step 2: Verify each integration**
  - **Android Devices → Scan Network** — should report mDNS hits, no "adb not found" errors.
  - **Android Devices → Connect a known device → Toggle Power** — adb commands run through resolver; verify the request succeeds and the device responds.
  - **Cameras** — go2rtc should auto-spawn (already local); verify `/streams` page loads.
  - **ws-scrcpy-web** — open a device dashboard; iframe should load (node now resolves via resolver, but ws-scrcpy-web itself is the still-pending separate TODO — it remains user-configured manual path).
  - **Jellyfin → Cast/crew update worker** (if exposed) — verify it can run; sqlite3 path resolves.
  - **Settings → Dependencies → Refresh** — every dep should report a version (locally) or "Not found" (no PATH leakage).

- [ ] **Step 3: Stop the app**

`Ctrl+C` in the terminal.

---

### Task 6.4: Final commit + update memory

- [ ] **Step 1: Update todo file**

Move the "Local-Dependencies-Only audit" item in `C:/Users/jscha/.claude/projects/C--Users-jscha/memory/todo_control_menu.md` from the active section to the **Shipped (recent reference)** section, with the commit range and test-count delta.

- [ ] **Step 2: Commit memory + any final touch-ups**

```bash
# (memory file is outside the repo; commit any in-repo final changes)
git status
git commit -am "chore(deps): finalize local-deps audit fix"
```

- [ ] **Step 3: Optional: open a PR**

Per `superpowers:finishing-a-development-branch`. Choose merge-to-master vs. PR depending on user preference.

---

## Self-Review Notes

- **Spec coverage:** every audit-table row from the TODO has an explicit task. AdbService → Phase 2. WsScrcpy node → 3.1. Jellyfin sqlite3 → 3.2. Dep manager adb kill-server → 3.3. tar → 3.4. PATH probing + common locations + GetInstalledVersion fallback → Phase 4. Allowlist documentation → Phase 5.
- **Type consistency:** resolver method is `ResolveAsync(moduleId, name, ct)` everywhere. Extension method is `ExecuteResolvedAsync(resolver, moduleId, name, args, workingDir?, ct?)`. Exception is `DependencyNotInstalledException` everywhere.
- **Parallelization for `/build-with-agent-team`:** Phase 0 + Phase 1 are serial (the contract). After Phase 1 is on master/branch, Phase 2 (AdbService), Phase 3.1 (WsScrcpy), Phase 3.2 (Jellyfin sqlite3), Phase 3.3 (adb kill-server), Phase 3.4 (tar) can each run as an independent agent — they touch disjoint files. Phase 4 must wait for Phase 3.3 + 3.4 to land (it touches the same file, `DependencyManagerService.cs`). Phase 5 + 6 are post-merge.
- **Risks:** `WsScrcpyService` async/sync split (Task 3.1 Step 4) is the most fragile — `SpawnProcess` is called under a lock in sync code today. Resolving once at startup-time is the cleanest fix; a sloppy implementation that calls `_resolver.ResolveAsync(...).Result` inside the lock would deadlock. The plan calls this out explicitly.
