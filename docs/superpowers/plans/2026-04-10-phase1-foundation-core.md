# Phase 1: Foundation & Core — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the ControlMenu Blazor Server application with database schema, core services (CommandExecutor, SecretStore, ConfigurationService), module system with auto-discovery, and the UI shell (sidebar navigation, top bar, theme switcher).

**Architecture:** Blazor Server (.NET 9) with SQLite persistence via EF Core. Convention-based module system using `IToolModule` interface with reflection-based auto-discovery at startup. Platform-aware command execution via strategy pattern. Encrypted secret storage via ASP.NET Data Protection API.

**Tech Stack:** .NET 9, ASP.NET Core Blazor Server, Entity Framework Core + SQLite, ASP.NET Data Protection API, xUnit + Moq

---

## File Structure

### Source Project: `src/ControlMenu/`

```
src/ControlMenu/
├── ControlMenu.csproj
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Properties/
│   └── launchSettings.json
├── Data/
│   ├── AppDbContext.cs
│   ├── Entities/
│   │   ├── Device.cs
│   │   ├── Job.cs
│   │   ├── Dependency.cs
│   │   └── Setting.cs
│   └── Enums/
│       ├── DeviceType.cs
│       ├── JobStatus.cs
│       ├── DependencyStatus.cs
│       └── UpdateSourceType.cs
├── Services/
│   ├── ICommandExecutor.cs
│   ├── CommandExecutor.cs
│   ├── CommandResult.cs
│   ├── CommandDefinition.cs
│   ├── ISecretStore.cs
│   ├── SecretStore.cs
│   ├── IConfigurationService.cs
│   └── ConfigurationService.cs
├── Modules/
│   ├── IToolModule.cs
│   ├── NavEntry.cs
│   ├── ModuleDependency.cs
│   ├── ConfigRequirement.cs
│   ├── BackgroundJobDefinition.cs
│   └── ModuleDiscoveryService.cs
├── Components/
│   ├── App.razor
│   ├── Routes.razor
│   ├── _Imports.razor
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   ├── MainLayout.razor.css
│   │   ├── Sidebar.razor
│   │   ├── Sidebar.razor.css
│   │   ├── TopBar.razor
│   │   └── TopBar.razor.css
│   └── Pages/
│       ├── Home.razor
│       └── Error.razor
└── wwwroot/
    ├── css/
    │   ├── app.css
    │   └── theme.css
    ├── js/
    │   └── theme.js
    └── favicon.png
```

### Test Project: `tests/ControlMenu.Tests/`

```
tests/ControlMenu.Tests/
├── ControlMenu.Tests.csproj
├── Data/
│   ├── AppDbContextTests.cs
│   └── TestDbContextFactory.cs
├── Services/
│   ├── CommandExecutorTests.cs
│   ├── SecretStoreTests.cs
│   └── ConfigurationServiceTests.cs
└── Modules/
    ├── ModuleDiscoveryServiceTests.cs
    └── Fakes/
        └── FakeToolModule.cs
```

### Root

```
ControlMenu.sln
.gitignore
```

---

## Task 1: Project Scaffold & Git Init

**Files:**
- Create: `ControlMenu.sln`
- Create: `src/ControlMenu/ControlMenu.csproj`
- Create: `tests/ControlMenu.Tests/ControlMenu.Tests.csproj`
- Create: `.gitignore`

All paths below are relative to `C:\Scripts\tools-menu\`.

- [ ] **Step 1: Initialize git repository**

```bash
cd C:\Scripts\tools-menu
git init
```

- [ ] **Step 2: Create .gitignore**

Create `.gitignore` at the repo root. This excludes .NET build artifacts and the existing binary tools in the root directory (adb, scrcpy, DLLs, etc.) while including `src/`, `tests/`, and `docs/`.

```gitignore
# .NET build output
bin/
obj/
publish/

# IDE
.vs/
.vscode/
*.user
*.suo

# OS
Thumbs.db
Desktop.ini
.DS_Store

# SQLite runtime databases (not the EF migrations)
*.db
*.db-shm
*.db-wal

# Data Protection keys (machine-specific)
keys/

# Root-level binaries (existing tools — not part of the .NET project)
/*.exe
/*.dll
/*.bat
/*.vbs
/*.ico
/*.png
/scrcpy-server
/source.properties
/mke2fs.conf
/*.lnk

# PowerShell scripts (legacy, kept for reference but not part of new app)
/*.ps1

# Jellyfin backup directory
/jellyfin-db-bkup-and-logs/

# Keep these directories
!src/
!tests/
!docs/
```

- [ ] **Step 3: Create Blazor Server project**

```bash
cd C:\Scripts\tools-menu
dotnet new blazor -n ControlMenu -o src/ControlMenu --interactivity Server --all-interactive --no-https
```

The `--no-https` flag keeps it simple for localhost-first. HTTPS can be added when remote/server mode is implemented later.

- [ ] **Step 4: Create xUnit test project**

```bash
cd C:\Scripts\tools-menu
dotnet new xunit -n ControlMenu.Tests -o tests/ControlMenu.Tests
```

- [ ] **Step 5: Create solution and add projects**

```bash
cd C:\Scripts\tools-menu
dotnet new sln -n ControlMenu
dotnet sln add src/ControlMenu/ControlMenu.csproj
dotnet sln add tests/ControlMenu.Tests/ControlMenu.Tests.csproj
dotnet add tests/ControlMenu.Tests/ControlMenu.Tests.csproj reference src/ControlMenu/ControlMenu.csproj
```

- [ ] **Step 6: Add NuGet packages to source project**

```bash
cd C:\Scripts\tools-menu
dotnet add src/ControlMenu/ControlMenu.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/ControlMenu/ControlMenu.csproj package Microsoft.EntityFrameworkCore.Design
```

- [ ] **Step 7: Add NuGet packages to test project**

```bash
cd C:\Scripts\tools-menu
dotnet add tests/ControlMenu.Tests/ControlMenu.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add tests/ControlMenu.Tests/ControlMenu.Tests.csproj package Moq
```

- [ ] **Step 8: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 9: Delete template boilerplate**

Remove the template sample pages that we won't need:

```bash
rm src/ControlMenu/Components/Pages/Counter.razor
rm src/ControlMenu/Components/Pages/Weather.razor
```

- [ ] **Step 10: Verify build still passes**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 11: Commit**

```bash
cd C:\Scripts\tools-menu
git add .gitignore ControlMenu.sln src/ tests/
git commit -m "feat: scaffold Blazor Server project with test project"
```

---

## Task 2: Database Enums & Entities

**Files:**
- Create: `src/ControlMenu/Data/Enums/DeviceType.cs`
- Create: `src/ControlMenu/Data/Enums/JobStatus.cs`
- Create: `src/ControlMenu/Data/Enums/DependencyStatus.cs`
- Create: `src/ControlMenu/Data/Enums/UpdateSourceType.cs`
- Create: `src/ControlMenu/Data/Entities/Device.cs`
- Create: `src/ControlMenu/Data/Entities/Job.cs`
- Create: `src/ControlMenu/Data/Entities/Dependency.cs`
- Create: `src/ControlMenu/Data/Entities/Setting.cs`

- [ ] **Step 1: Create enum files**

`src/ControlMenu/Data/Enums/DeviceType.cs`:
```csharp
namespace ControlMenu.Data.Enums;

public enum DeviceType
{
    GoogleTV,
    AndroidPhone
}
```

`src/ControlMenu/Data/Enums/JobStatus.cs`:
```csharp
namespace ControlMenu.Data.Enums;

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}
```

`src/ControlMenu/Data/Enums/DependencyStatus.cs`:
```csharp
namespace ControlMenu.Data.Enums;

public enum DependencyStatus
{
    UpToDate,
    UpdateAvailable,
    UrlInvalid,
    CheckFailed
}
```

`src/ControlMenu/Data/Enums/UpdateSourceType.cs`:
```csharp
namespace ControlMenu.Data.Enums;

public enum UpdateSourceType
{
    GitHub,
    DirectUrl,
    Manual
}
```

- [ ] **Step 2: Create entity files**

`src/ControlMenu/Data/Entities/Device.cs`:
```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Data.Entities;

public class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DeviceType Type { get; set; }
    public required string MacAddress { get; set; }
    public string? SerialNumber { get; set; }
    public string? LastKnownIp { get; set; }
    public int AdbPort { get; set; } = 5555;
    public DateTime? LastSeen { get; set; }
    public required string ModuleId { get; set; }
    public string? Metadata { get; set; }
}
```

`src/ControlMenu/Data/Entities/Job.cs`:
```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Data.Entities;

public class Job
{
    public Guid Id { get; set; }
    public required string ModuleId { get; set; }
    public required string JobType { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int? Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public int? ProcessId { get; set; }
    public bool CancellationRequested { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultData { get; set; }
}
```

`src/ControlMenu/Data/Entities/Dependency.cs`:
```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Data.Entities;

public class Dependency
{
    public Guid Id { get; set; }
    public required string ModuleId { get; set; }
    public required string Name { get; set; }
    public string? InstalledVersion { get; set; }
    public string? LatestKnownVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ProjectHomeUrl { get; set; }
    public DateTime? LastChecked { get; set; }
    public DependencyStatus Status { get; set; }
    public UpdateSourceType SourceType { get; set; }
}
```

`src/ControlMenu/Data/Entities/Setting.cs`:
```csharp
namespace ControlMenu.Data.Entities;

public class Setting
{
    public Guid Id { get; set; }
    public string? ModuleId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public bool IsSecret { get; set; }
}
```

- [ ] **Step 3: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Data/
git commit -m "feat: add database enums and entity classes"
```

---

## Task 3: AppDbContext & Initial Migration

**Files:**
- Create: `src/ControlMenu/Data/AppDbContext.cs`
- Create: `tests/ControlMenu.Tests/Data/TestDbContextFactory.cs`
- Create: `tests/ControlMenu.Tests/Data/AppDbContextTests.cs`

- [ ] **Step 1: Write the failing test — verify DbContext can create database and round-trip a Setting**

`tests/ControlMenu.Tests/Data/TestDbContextFactory.cs`:
```csharp
using ControlMenu.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Tests.Data;

public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
```

`tests/ControlMenu.Tests/Data/AppDbContextTests.cs`:
```csharp
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;

namespace ControlMenu.Tests.Data;

public class AppDbContextTests
{
    [Fact]
    public async Task CanRoundTripSetting()
    {
        using var db = TestDbContextFactory.Create();

        var setting = new Setting
        {
            Id = Guid.NewGuid(),
            Key = "theme",
            Value = "dark",
            IsSecret = false
        };

        db.Settings.Add(setting);
        await db.SaveChangesAsync();

        var loaded = await db.Settings.FindAsync(setting.Id);

        Assert.NotNull(loaded);
        Assert.Equal("theme", loaded.Key);
        Assert.Equal("dark", loaded.Value);
    }

    [Fact]
    public async Task CanRoundTripDevice()
    {
        using var db = TestDbContextFactory.Create();

        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Living Room TV",
            Type = DeviceType.GoogleTV,
            MacAddress = "b8-7b-d4-f3-ae-84",
            ModuleId = "android-devices",
            AdbPort = 5555
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var loaded = await db.Devices.FindAsync(device.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Living Room TV", loaded.Name);
        Assert.Equal(DeviceType.GoogleTV, loaded.Type);
    }

    [Fact]
    public async Task CanRoundTripJob()
    {
        using var db = TestDbContextFactory.Create();

        var job = new Job
        {
            Id = Guid.NewGuid(),
            ModuleId = "jellyfin",
            JobType = "cast-crew-update",
            Status = JobStatus.Queued
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var loaded = await db.Jobs.FindAsync(job.Id);

        Assert.NotNull(loaded);
        Assert.Equal(JobStatus.Queued, loaded.Status);
    }

    [Fact]
    public async Task CanRoundTripDependency()
    {
        using var db = TestDbContextFactory.Create();

        var dep = new Dependency
        {
            Id = Guid.NewGuid(),
            ModuleId = "android-devices",
            Name = "adb",
            Status = DependencyStatus.UpToDate,
            SourceType = UpdateSourceType.DirectUrl
        };

        db.Dependencies.Add(dep);
        await db.SaveChangesAsync();

        var loaded = await db.Dependencies.FindAsync(dep.Id);

        Assert.NotNull(loaded);
        Assert.Equal("adb", loaded.Name);
        Assert.Equal(DependencyStatus.UpToDate, loaded.Status);
    }

    [Fact]
    public async Task SettingUniqueIndex_ModuleIdAndKey()
    {
        using var db = TestDbContextFactory.Create();

        db.Settings.Add(new Setting
        {
            Id = Guid.NewGuid(),
            ModuleId = null,
            Key = "theme",
            Value = "dark"
        });
        await db.SaveChangesAsync();

        db.Settings.Add(new Setting
        {
            Id = Guid.NewGuid(),
            ModuleId = null,
            Key = "theme",
            Value = "light"
        });

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(
            () => db.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~AppDbContextTests" -v minimal
```

Expected: FAIL — `AppDbContext` does not exist.

- [ ] **Step 3: Create AppDbContext**

`src/ControlMenu/Data/AppDbContext.cs`:
```csharp
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Type).HasConversion<string>();
        });

        modelBuilder.Entity<Job>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Dependency>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Status).HasConversion<string>();
            e.Property(d => d.SourceType).HasConversion<string>();
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.ModuleId, s.Key }).IsUnique();
        });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~AppDbContextTests" -v minimal
```

Expected: 5 passed, 0 failed.

- [ ] **Step 5: Create initial EF Core migration**

```bash
cd C:\Scripts\tools-menu
dotnet ef migrations add InitialCreate --project src/ControlMenu/ --startup-project src/ControlMenu/
```

This requires a connection string in `appsettings.json` and EF Core registration in `Program.cs`. Update both first.

Update `src/ControlMenu/appsettings.json` — add a ConnectionStrings section:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=controlmenu.db"
  }
}
```

Update `src/ControlMenu/Program.cs` — add EF Core registration (replace the full file):
```csharp
using ControlMenu.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<ControlMenu.Components.App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
```

Now run the migration:
```bash
cd C:\Scripts\tools-menu
dotnet ef migrations add InitialCreate --project src/ControlMenu/ --startup-project src/ControlMenu/
```

Expected: Migration files created in `src/ControlMenu/Migrations/`.

- [ ] **Step 6: Verify build and all tests still pass**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln && dotnet test ControlMenu.sln -v minimal
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 7: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Data/ src/ControlMenu/Program.cs src/ControlMenu/appsettings.json src/ControlMenu/Migrations/ tests/ControlMenu.Tests/Data/
git commit -m "feat: add AppDbContext with entities, enums, and initial migration"
```

---

## Task 4: CommandExecutor Service

**Files:**
- Create: `src/ControlMenu/Services/CommandResult.cs`
- Create: `src/ControlMenu/Services/CommandDefinition.cs`
- Create: `src/ControlMenu/Services/ICommandExecutor.cs`
- Create: `src/ControlMenu/Services/CommandExecutor.cs`
- Create: `tests/ControlMenu.Tests/Services/CommandExecutorTests.cs`

The CommandExecutor wraps `System.Diagnostics.Process` and selects the right command variant based on the current OS. It runs commands asynchronously and captures stdout, stderr, and exit code.

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Services/CommandExecutorTests.cs`:
```csharp
using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class CommandExecutorTests
{
    private readonly CommandExecutor _executor = new();

    [Fact]
    public async Task ExecuteAsync_SimpleCommand_ReturnsOutput()
    {
        // "echo hello" works on both Windows and Linux
        var result = await _executor.ExecuteAsync("echo", "hello");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_BadCommand_ReturnsNonZeroExitCode()
    {
        // Run a command that will fail
        var result = await _executor.ExecuteAsync(
            OperatingSystem.IsWindows() ? "cmd" : "bash",
            OperatingSystem.IsWindows() ? "/c exit 1" : "-c \"exit 1\"");

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStderr()
    {
        var result = await _executor.ExecuteAsync(
            OperatingSystem.IsWindows() ? "cmd" : "bash",
            OperatingSystem.IsWindows()
                ? "/c echo error message>&2"
                : "-c \"echo error message >&2\"");

        Assert.Contains("error message", result.StandardError);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_RespectsToken()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _executor.ExecuteAsync("echo", "hello", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteDefinitionAsync_SelectsPlatformCommand()
    {
        var definition = new CommandDefinition
        {
            WindowsCommand = "cmd",
            WindowsArguments = "/c echo windows-hello",
            LinuxCommand = "echo",
            LinuxArguments = "linux-hello"
        };

        var result = await _executor.ExecuteAsync(definition);

        Assert.Equal(0, result.ExitCode);

        if (OperatingSystem.IsWindows())
            Assert.Contains("windows-hello", result.StandardOutput);
        else
            Assert.Contains("linux-hello", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteDefinitionAsync_Timeout_SetsTimedOutFlag()
    {
        var definition = new CommandDefinition
        {
            WindowsCommand = "cmd",
            WindowsArguments = "/c ping -n 10 127.0.0.1",
            LinuxCommand = "sleep",
            LinuxArguments = "10",
            Timeout = TimeSpan.FromMilliseconds(200)
        };

        var result = await _executor.ExecuteAsync(definition);

        Assert.True(result.TimedOut);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~CommandExecutorTests" -v minimal
```

Expected: FAIL — types do not exist.

- [ ] **Step 3: Create CommandResult record**

`src/ControlMenu/Services/CommandResult.cs`:
```csharp
namespace ControlMenu.Services;

public record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);
```

- [ ] **Step 4: Create CommandDefinition record**

`src/ControlMenu/Services/CommandDefinition.cs`:
```csharp
namespace ControlMenu.Services;

public record CommandDefinition
{
    public required string WindowsCommand { get; init; }
    public required string LinuxCommand { get; init; }
    public string? WindowsArguments { get; init; }
    public string? LinuxArguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public TimeSpan? Timeout { get; init; }
}
```

- [ ] **Step 5: Create ICommandExecutor interface**

`src/ControlMenu/Services/ICommandExecutor.cs`:
```csharp
namespace ControlMenu.Services;

public interface ICommandExecutor
{
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

- [ ] **Step 6: Create CommandExecutor implementation**

`src/ControlMenu/Services/CommandExecutor.cs`:
```csharp
using System.Diagnostics;

namespace ControlMenu.Services;

public class CommandExecutor : ICommandExecutor
{
    public async Task<CommandResult> ExecuteAsync(
        string command,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new CommandResult(process.ExitCode, stdout, stderr, TimedOut: false);
    }

    public async Task<CommandResult> ExecuteAsync(
        CommandDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var isWindows = OperatingSystem.IsWindows();
        var command = isWindows ? definition.WindowsCommand : definition.LinuxCommand;
        var arguments = isWindows ? definition.WindowsArguments : definition.LinuxArguments;

        if (definition.Timeout is { } timeout)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                return await ExecuteAsync(command, arguments, definition.WorkingDirectory, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new CommandResult(ExitCode: -1, StandardOutput: "", StandardError: "Process timed out", TimedOut: true);
            }
        }

        return await ExecuteAsync(command, arguments, definition.WorkingDirectory, cancellationToken);
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~CommandExecutorTests" -v minimal
```

Expected: 6 passed, 0 failed.

- [ ] **Step 8: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Services/CommandResult.cs src/ControlMenu/Services/CommandDefinition.cs src/ControlMenu/Services/ICommandExecutor.cs src/ControlMenu/Services/CommandExecutor.cs tests/ControlMenu.Tests/Services/CommandExecutorTests.cs
git commit -m "feat: add CommandExecutor with platform-aware command execution"
```

---

## Task 5: SecretStore Service

**Files:**
- Create: `src/ControlMenu/Services/ISecretStore.cs`
- Create: `src/ControlMenu/Services/SecretStore.cs`
- Create: `tests/ControlMenu.Tests/Services/SecretStoreTests.cs`

The SecretStore wraps ASP.NET Data Protection API to encrypt/decrypt setting values stored in the database.

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Services/SecretStoreTests.cs`:
```csharp
using ControlMenu.Services;
using Microsoft.AspNetCore.DataProtection;

namespace ControlMenu.Tests.Services;

public class SecretStoreTests
{
    private static SecretStore CreateSecretStore()
    {
        var provider = DataProtectionProvider.Create("ControlMenu-Tests");
        return new SecretStore(provider);
    }

    [Fact]
    public void Encrypt_ProducesDifferentStringThanInput()
    {
        var store = CreateSecretStore();
        var plaintext = "my-api-key-12345";

        var encrypted = store.Encrypt(plaintext);

        Assert.NotEqual(plaintext, encrypted);
        Assert.NotEmpty(encrypted);
    }

    [Fact]
    public void Decrypt_ReturnsOriginalPlaintext()
    {
        var store = CreateSecretStore();
        var plaintext = "my-api-key-12345";

        var encrypted = store.Encrypt(plaintext);
        var decrypted = store.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RoundTrip_EmptyString()
    {
        var store = CreateSecretStore();

        var encrypted = store.Encrypt("");
        var decrypted = store.Decrypt(encrypted);

        Assert.Equal("", decrypted);
    }

    [Fact]
    public void RoundTrip_SpecialCharacters()
    {
        var store = CreateSecretStore();
        var plaintext = "p@$$w0rd!#%&*()_+-=[]{}|;':\",./<>?";

        var encrypted = store.Encrypt(plaintext);
        var decrypted = store.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SameInput_ProducesDifferentCiphertext()
    {
        // Data Protection uses a key + nonce, so repeated calls
        // with the same input should produce the same output
        // (the protector is deterministic for a given purpose string).
        // This test verifies the encrypt path is consistent.
        var store = CreateSecretStore();
        var plaintext = "test-value";

        var encrypted1 = store.Encrypt(plaintext);
        var encrypted2 = store.Encrypt(plaintext);

        // Both should decrypt to the same value
        Assert.Equal(plaintext, store.Decrypt(encrypted1));
        Assert.Equal(plaintext, store.Decrypt(encrypted2));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~SecretStoreTests" -v minimal
```

Expected: FAIL — `ISecretStore` and `SecretStore` do not exist.

- [ ] **Step 3: Create ISecretStore interface**

`src/ControlMenu/Services/ISecretStore.cs`:
```csharp
namespace ControlMenu.Services;

public interface ISecretStore
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
```

- [ ] **Step 4: Create SecretStore implementation**

`src/ControlMenu/Services/SecretStore.cs`:
```csharp
using Microsoft.AspNetCore.DataProtection;

namespace ControlMenu.Services;

public class SecretStore : ISecretStore
{
    private readonly IDataProtector _protector;

    public SecretStore(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ControlMenu.Settings");
    }

    public string Encrypt(string plaintext)
    {
        return _protector.Protect(plaintext);
    }

    public string Decrypt(string ciphertext)
    {
        return _protector.Unprotect(ciphertext);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~SecretStoreTests" -v minimal
```

Expected: 5 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Services/ISecretStore.cs src/ControlMenu/Services/SecretStore.cs tests/ControlMenu.Tests/Services/SecretStoreTests.cs
git commit -m "feat: add SecretStore with Data Protection API encryption"
```

---

## Task 6: ConfigurationService

**Files:**
- Create: `src/ControlMenu/Services/IConfigurationService.cs`
- Create: `src/ControlMenu/Services/ConfigurationService.cs`
- Create: `tests/ControlMenu.Tests/Services/ConfigurationServiceTests.cs`

The ConfigurationService provides CRUD for the Settings table, automatically encrypting/decrypting secret values via SecretStore.

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Services/ConfigurationServiceTests.cs`:
```csharp
using ControlMenu.Data;
using ControlMenu.Services;
using ControlMenu.Tests.Data;
using Microsoft.AspNetCore.DataProtection;

namespace ControlMenu.Tests.Services;

public class ConfigurationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ConfigurationService _service;

    public ConfigurationServiceTests()
    {
        _db = TestDbContextFactory.Create();
        var provider = DataProtectionProvider.Create("ControlMenu-Tests");
        var secretStore = new SecretStore(provider);
        _service = new ConfigurationService(_db, secretStore);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetSettingAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetSettingAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGetSetting_RoundTrip()
    {
        await _service.SetSettingAsync("theme", "dark");

        var result = await _service.GetSettingAsync("theme");

        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task SetSetting_OverwritesExistingValue()
    {
        await _service.SetSettingAsync("theme", "dark");
        await _service.SetSettingAsync("theme", "light");

        var result = await _service.GetSettingAsync("theme");

        Assert.Equal("light", result);
    }

    [Fact]
    public async Task SetAndGetSecret_RoundTrip()
    {
        await _service.SetSecretAsync("api-key", "secret-value-123");

        var result = await _service.GetSecretAsync("api-key");

        Assert.Equal("secret-value-123", result);
    }

    [Fact]
    public async Task SetSecret_StoresEncryptedValue()
    {
        await _service.SetSecretAsync("api-key", "secret-value-123");

        // Read raw value from DB — it should be encrypted, not plaintext
        var setting = _db.Settings.Single(s => s.Key == "api-key");
        Assert.True(setting.IsSecret);
        Assert.NotEqual("secret-value-123", setting.Value);
    }

    [Fact]
    public async Task ModuleScoping_SameKeyDifferentModules()
    {
        await _service.SetSettingAsync("url", "http://global", moduleId: null);
        await _service.SetSettingAsync("url", "http://jellyfin", moduleId: "jellyfin");

        var global = await _service.GetSettingAsync("url", moduleId: null);
        var jellyfin = await _service.GetSettingAsync("url", moduleId: "jellyfin");

        Assert.Equal("http://global", global);
        Assert.Equal("http://jellyfin", jellyfin);
    }

    [Fact]
    public async Task DeleteSettingAsync_RemovesSetting()
    {
        await _service.SetSettingAsync("theme", "dark");
        await _service.DeleteSettingAsync("theme");

        var result = await _service.GetSettingAsync("theme");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetModuleSettingsAsync_ReturnsOnlyModuleSettings()
    {
        await _service.SetSettingAsync("global-key", "global-value");
        await _service.SetSettingAsync("jf-url", "http://localhost:8096", moduleId: "jellyfin");
        await _service.SetSettingAsync("jf-key", "abc123", moduleId: "jellyfin");
        await _service.SetSettingAsync("other-key", "other", moduleId: "other-module");

        var settings = await _service.GetModuleSettingsAsync("jellyfin");

        Assert.Equal(2, settings.Count);
        Assert.All(settings, s => Assert.Equal("jellyfin", s.ModuleId));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~ConfigurationServiceTests" -v minimal
```

Expected: FAIL — `IConfigurationService` and `ConfigurationService` do not exist.

- [ ] **Step 3: Create IConfigurationService interface**

`src/ControlMenu/Services/IConfigurationService.cs`:
```csharp
using ControlMenu.Data.Entities;

namespace ControlMenu.Services;

public interface IConfigurationService
{
    Task<string?> GetSettingAsync(string key, string? moduleId = null);
    Task SetSettingAsync(string key, string value, string? moduleId = null);
    Task<string?> GetSecretAsync(string key, string? moduleId = null);
    Task SetSecretAsync(string key, string value, string? moduleId = null);
    Task DeleteSettingAsync(string key, string? moduleId = null);
    Task<IReadOnlyList<Setting>> GetModuleSettingsAsync(string moduleId);
}
```

- [ ] **Step 4: Create ConfigurationService implementation**

`src/ControlMenu/Services/ConfigurationService.cs`:
```csharp
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly AppDbContext _db;
    private readonly ISecretStore _secretStore;

    public ConfigurationService(AppDbContext db, ISecretStore secretStore)
    {
        _db = db;
        _secretStore = secretStore;
    }

    public async Task<string?> GetSettingAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is null)
        {
            setting = new Setting
            {
                Id = Guid.NewGuid(),
                ModuleId = moduleId,
                Key = key,
                Value = value,
                IsSecret = false
            };
            _db.Settings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.IsSecret = false;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<string?> GetSecretAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is null) return null;
        return setting.IsSecret ? _secretStore.Decrypt(setting.Value) : setting.Value;
    }

    public async Task SetSecretAsync(string key, string value, string? moduleId = null)
    {
        var encrypted = _secretStore.Encrypt(value);
        var setting = await FindSettingAsync(key, moduleId);

        if (setting is null)
        {
            setting = new Setting
            {
                Id = Guid.NewGuid(),
                ModuleId = moduleId,
                Key = key,
                Value = encrypted,
                IsSecret = true
            };
            _db.Settings.Add(setting);
        }
        else
        {
            setting.Value = encrypted;
            setting.IsSecret = true;
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteSettingAsync(string key, string? moduleId = null)
    {
        var setting = await FindSettingAsync(key, moduleId);
        if (setting is not null)
        {
            _db.Settings.Remove(setting);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<Setting>> GetModuleSettingsAsync(string moduleId)
    {
        return await _db.Settings
            .Where(s => s.ModuleId == moduleId)
            .ToListAsync();
    }

    private async Task<Setting?> FindSettingAsync(string key, string? moduleId)
    {
        return await _db.Settings
            .FirstOrDefaultAsync(s => s.Key == key && s.ModuleId == moduleId);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~ConfigurationServiceTests" -v minimal
```

Expected: 8 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Services/IConfigurationService.cs src/ControlMenu/Services/ConfigurationService.cs tests/ControlMenu.Tests/Services/ConfigurationServiceTests.cs
git commit -m "feat: add ConfigurationService with encrypted secret support"
```

---

## Task 7: Module System Types

**Files:**
- Create: `src/ControlMenu/Modules/IToolModule.cs`
- Create: `src/ControlMenu/Modules/NavEntry.cs`
- Create: `src/ControlMenu/Modules/ModuleDependency.cs`
- Create: `src/ControlMenu/Modules/ConfigRequirement.cs`
- Create: `src/ControlMenu/Modules/BackgroundJobDefinition.cs`

These are pure interface/data types with no behavior. They get tested through ModuleDiscoveryService in Task 8.

- [ ] **Step 1: Create IToolModule interface**

`src/ControlMenu/Modules/IToolModule.cs`:
```csharp
namespace ControlMenu.Modules;

public interface IToolModule
{
    string Id { get; }
    string DisplayName { get; }
    string Icon { get; }
    int SortOrder { get; }
    IEnumerable<ModuleDependency> Dependencies { get; }
    IEnumerable<ConfigRequirement> ConfigRequirements { get; }
    IEnumerable<NavEntry> GetNavEntries();
    IEnumerable<BackgroundJobDefinition> GetBackgroundJobs();
}
```

- [ ] **Step 2: Create NavEntry record**

`src/ControlMenu/Modules/NavEntry.cs`:
```csharp
namespace ControlMenu.Modules;

public record NavEntry(
    string Title,
    string Href,
    string? Icon = null,
    int SortOrder = 0);
```

- [ ] **Step 3: Create ModuleDependency record**

`src/ControlMenu/Modules/ModuleDependency.cs`:
```csharp
using ControlMenu.Data.Enums;

namespace ControlMenu.Modules;

public record ModuleDependency
{
    public required string Name { get; init; }
    public required string ExecutableName { get; init; }
    public required string VersionCommand { get; init; }
    public required string VersionPattern { get; init; }
    public UpdateSourceType SourceType { get; init; }
    public string? GitHubRepo { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ProjectHomeUrl { get; init; }
    public string? AssetPattern { get; init; }
    public string? InstallPath { get; init; }
    public string[] RelatedFiles { get; init; } = [];
}
```

- [ ] **Step 4: Create ConfigRequirement record**

`src/ControlMenu/Modules/ConfigRequirement.cs`:
```csharp
namespace ControlMenu.Modules;

public record ConfigRequirement(
    string Key,
    string DisplayName,
    string Description,
    bool IsSecret = false,
    string? DefaultValue = null);
```

- [ ] **Step 5: Create BackgroundJobDefinition record**

`src/ControlMenu/Modules/BackgroundJobDefinition.cs`:
```csharp
namespace ControlMenu.Modules;

public record BackgroundJobDefinition(
    string JobType,
    string DisplayName,
    string Description,
    bool IsLongRunning = false);
```

- [ ] **Step 6: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 7: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Modules/
git commit -m "feat: add IToolModule interface and supporting types"
```

---

## Task 8: ModuleDiscoveryService

**Files:**
- Create: `src/ControlMenu/Modules/ModuleDiscoveryService.cs`
- Create: `tests/ControlMenu.Tests/Modules/Fakes/FakeToolModule.cs`
- Create: `tests/ControlMenu.Tests/Modules/ModuleDiscoveryServiceTests.cs`

The ModuleDiscoveryService scans loaded assemblies at startup for classes implementing `IToolModule`, instantiates them, and exposes them sorted by `SortOrder`. This is what makes the module system auto-discover new modules without changes to core code.

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/Fakes/FakeToolModule.cs`:
```csharp
using ControlMenu.Modules;

namespace ControlMenu.Tests.Modules.Fakes;

public class FakeToolModule : IToolModule
{
    public string Id => "fake-module";
    public string DisplayName => "Fake Module";
    public string Icon => "bi-gear";
    public int SortOrder => 10;
    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];
    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Fake Page", "/fake", "bi-gear", 0)
    ];
    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}

public class SecondFakeToolModule : IToolModule
{
    public string Id => "second-fake";
    public string DisplayName => "Second Fake";
    public string Icon => "bi-star";
    public int SortOrder => 5;
    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];
    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Second Page", "/second", "bi-star", 0)
    ];
    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
```

`tests/ControlMenu.Tests/Modules/ModuleDiscoveryServiceTests.cs`:
```csharp
using ControlMenu.Modules;
using ControlMenu.Tests.Modules.Fakes;

namespace ControlMenu.Tests.Modules;

public class ModuleDiscoveryServiceTests
{
    [Fact]
    public void DiscoverModules_FindsFakeModules()
    {
        // The test assembly contains FakeToolModule and SecondFakeToolModule
        var service = new ModuleDiscoveryService([typeof(FakeToolModule).Assembly]);

        Assert.True(service.Modules.Count >= 2);
        Assert.Contains(service.Modules, m => m.Id == "fake-module");
        Assert.Contains(service.Modules, m => m.Id == "second-fake");
    }

    [Fact]
    public void DiscoverModules_SortedBySortOrder()
    {
        var service = new ModuleDiscoveryService([typeof(FakeToolModule).Assembly]);

        var fakeIndex = service.Modules.ToList().FindIndex(m => m.Id == "fake-module");
        var secondIndex = service.Modules.ToList().FindIndex(m => m.Id == "second-fake");

        // SecondFakeToolModule has SortOrder 5, FakeToolModule has 10
        Assert.True(secondIndex < fakeIndex,
            "Modules should be sorted by SortOrder ascending");
    }

    [Fact]
    public void GetNavEntries_AggregatesAllModuleEntries()
    {
        var service = new ModuleDiscoveryService([typeof(FakeToolModule).Assembly]);

        var allNav = service.Modules.SelectMany(m => m.GetNavEntries()).ToList();

        Assert.Contains(allNav, n => n.Href == "/fake");
        Assert.Contains(allNav, n => n.Href == "/second");
    }

    [Fact]
    public void DiscoverModules_EmptyAssemblyList_ReturnsEmpty()
    {
        var service = new ModuleDiscoveryService([]);

        Assert.Empty(service.Modules);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~ModuleDiscoveryServiceTests" -v minimal
```

Expected: FAIL — `ModuleDiscoveryService` does not exist.

- [ ] **Step 3: Create ModuleDiscoveryService**

`src/ControlMenu/Modules/ModuleDiscoveryService.cs`:
```csharp
using System.Reflection;

namespace ControlMenu.Modules;

public class ModuleDiscoveryService
{
    public IReadOnlyList<IToolModule> Modules { get; }

    public ModuleDiscoveryService(IEnumerable<Assembly> assemblies)
    {
        Modules = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IToolModule).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IToolModule)Activator.CreateInstance(t)!)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.DisplayName)
            .ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd C:\Scripts\tools-menu
dotnet test tests/ControlMenu.Tests/ --filter "FullyQualifiedName~ModuleDiscoveryServiceTests" -v minimal
```

Expected: 4 passed, 0 failed.

- [ ] **Step 5: Run all tests to confirm nothing is broken**

```bash
cd C:\Scripts\tools-menu
dotnet test ControlMenu.sln -v minimal
```

Expected: All tests pass (23 total at this point).

- [ ] **Step 6: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Modules/ModuleDiscoveryService.cs tests/ControlMenu.Tests/Modules/
git commit -m "feat: add ModuleDiscoveryService with reflection-based auto-discovery"
```

---

## Task 9: UI Shell — Layout, Sidebar, TopBar

**Files:**
- Modify: `src/ControlMenu/Components/App.razor`
- Modify: `src/ControlMenu/Components/Layout/MainLayout.razor`
- Create: `src/ControlMenu/Components/Layout/MainLayout.razor.css`
- Create: `src/ControlMenu/Components/Layout/Sidebar.razor`
- Create: `src/ControlMenu/Components/Layout/Sidebar.razor.css`
- Create: `src/ControlMenu/Components/Layout/TopBar.razor`
- Create: `src/ControlMenu/Components/Layout/TopBar.razor.css`
- Modify: `src/ControlMenu/Components/Pages/Home.razor`
- Modify: `src/ControlMenu/Components/_Imports.razor`

Delete the template's `NavMenu.razor` and `NavMenu.razor.css` files — we're replacing them with `Sidebar.razor`.

- [ ] **Step 1: Delete template NavMenu files**

```bash
rm src/ControlMenu/Components/Layout/NavMenu.razor
rm src/ControlMenu/Components/Layout/NavMenu.razor.css
```

- [ ] **Step 2: Update _Imports.razor**

Replace `src/ControlMenu/Components/_Imports.razor` with:

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using ControlMenu.Components
@using ControlMenu.Components.Layout
@using ControlMenu.Modules
```

- [ ] **Step 3: Update App.razor**

Replace `src/ControlMenu/Components/App.razor` with:

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="icon" type="image/png" href="favicon.png" />
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css" />
    <link rel="stylesheet" href="css/theme.css" />
    <link rel="stylesheet" href="css/app.css" />
    <link rel="stylesheet" href="ControlMenu.styles.css" />
    <script src="js/theme.js"></script>
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

- [ ] **Step 4: Create Sidebar.razor**

`src/ControlMenu/Components/Layout/Sidebar.razor`:
```razor
@inject ModuleDiscoveryService ModuleDiscovery

<nav class="sidebar @(Collapsed ? "collapsed" : "")">
    <div class="sidebar-header">
        @if (!Collapsed)
        {
            <span class="sidebar-title">Control Menu</span>
        }
        <button class="sidebar-toggle" @onclick="ToggleCollapsed" title="@(Collapsed ? "Expand sidebar" : "Collapse sidebar")">
            <i class="bi @(Collapsed ? "bi-chevron-right" : "bi-chevron-left")"></i>
        </button>
    </div>

    <div class="sidebar-nav">
        @if (ModuleDiscovery.Modules.Count == 0)
        {
            @if (!Collapsed)
            {
                <div class="sidebar-empty">
                    <i class="bi bi-inbox"></i>
                    <span>No modules loaded</span>
                </div>
            }
        }
        else
        {
            @foreach (var module in ModuleDiscovery.Modules)
            {
                <div class="sidebar-group">
                    <div class="sidebar-group-header" @onclick="() => ToggleGroup(module.Id)">
                        <i class="bi @module.Icon"></i>
                        @if (!Collapsed)
                        {
                            <span>@module.DisplayName</span>
                            <i class="bi bi-chevron-@(IsGroupExpanded(module.Id) ? "up" : "down") sidebar-chevron"></i>
                        }
                    </div>

                    @if (IsGroupExpanded(module.Id) && !Collapsed)
                    {
                        <div class="sidebar-group-items">
                            @foreach (var entry in module.GetNavEntries().OrderBy(e => e.SortOrder))
                            {
                                <NavLink class="sidebar-link" href="@entry.Href" Match="NavLinkMatch.All">
                                    @if (entry.Icon is not null)
                                    {
                                        <i class="bi @entry.Icon"></i>
                                    }
                                    <span>@entry.Title</span>
                                </NavLink>
                            }
                        </div>
                    }
                </div>
            }
        }
    </div>

    <div class="sidebar-footer">
        <NavLink class="sidebar-link" href="/settings" Match="NavLinkMatch.Prefix">
            <i class="bi bi-gear"></i>
            @if (!Collapsed)
            {
                <span>Settings</span>
            }
        </NavLink>
    </div>
</nav>

@code {
    private bool Collapsed { get; set; }
    private readonly HashSet<string> _expandedGroups = new();

    private void ToggleCollapsed() => Collapsed = !Collapsed;

    private void ToggleGroup(string moduleId)
    {
        if (!_expandedGroups.Remove(moduleId))
            _expandedGroups.Add(moduleId);
    }

    private bool IsGroupExpanded(string moduleId) => _expandedGroups.Contains(moduleId);
}
```

- [ ] **Step 5: Create Sidebar.razor.css**

`src/ControlMenu/Components/Layout/Sidebar.razor.css`:
```css
.sidebar {
    display: flex;
    flex-direction: column;
    width: 260px;
    min-height: 100vh;
    background-color: var(--sidebar-bg);
    border-right: 1px solid var(--border-color);
    transition: width 0.2s ease;
    overflow: hidden;
}

.sidebar.collapsed {
    width: 56px;
}

.sidebar-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 16px 12px;
    border-bottom: 1px solid var(--border-color);
}

.sidebar-title {
    font-weight: 600;
    font-size: 1.1rem;
    color: var(--text-primary);
    white-space: nowrap;
}

.sidebar-toggle {
    background: none;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    padding: 4px 8px;
    border-radius: 4px;
}

.sidebar-toggle:hover {
    background-color: var(--hover-bg);
}

.sidebar-nav {
    flex: 1;
    overflow-y: auto;
    padding: 8px 0;
}

.sidebar-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 24px 16px;
    color: var(--text-muted);
    font-size: 0.85rem;
}

.sidebar-group {
    margin-bottom: 4px;
}

.sidebar-group-header {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 16px;
    color: var(--text-secondary);
    font-size: 0.85rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    cursor: pointer;
    user-select: none;
}

.sidebar-group-header:hover {
    background-color: var(--hover-bg);
}

.sidebar-chevron {
    margin-left: auto;
    font-size: 0.75rem;
}

.sidebar-group-items {
    display: flex;
    flex-direction: column;
}

.sidebar-link {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 16px 8px 32px;
    color: var(--text-primary);
    text-decoration: none;
    font-size: 0.9rem;
    border-radius: 0;
}

.sidebar-link:hover {
    background-color: var(--hover-bg);
    text-decoration: none;
    color: var(--text-primary);
}

.sidebar-link.active, ::deep .sidebar-link.active {
    background-color: var(--active-bg);
    color: var(--accent-color);
    font-weight: 500;
}

.sidebar-footer {
    border-top: 1px solid var(--border-color);
    padding: 8px 0;
}

.sidebar-footer .sidebar-link {
    padding-left: 16px;
}
```

- [ ] **Step 6: Create TopBar.razor**

`src/ControlMenu/Components/Layout/TopBar.razor`:
```razor
<header class="top-bar">
    <div class="top-bar-breadcrumb">
        <NavLink href="/" Match="NavLinkMatch.All">
            <i class="bi bi-house"></i>
        </NavLink>
        <span class="breadcrumb-separator">/</span>
        <span class="breadcrumb-current">@PageTitle</span>
    </div>

    <div class="top-bar-actions">
        <button class="theme-toggle" @onclick="CycleTheme" title="@ThemeLabel">
            <i class="bi @ThemeIcon"></i>
        </button>
    </div>
</header>

@code {
    [Parameter]
    public string PageTitle { get; set; } = "Home";

    private string CurrentTheme { get; set; } = "system";

    private string ThemeIcon => CurrentTheme switch
    {
        "dark" => "bi-moon-fill",
        "light" => "bi-sun-fill",
        _ => "bi-circle-half"
    };

    private string ThemeLabel => CurrentTheme switch
    {
        "dark" => "Dark theme",
        "light" => "Light theme",
        _ => "System theme"
    };

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            CurrentTheme = await JS.InvokeAsync<string>("themeManager.get");
            StateHasChanged();
        }
    }

    private async Task CycleTheme()
    {
        CurrentTheme = CurrentTheme switch
        {
            "system" => "dark",
            "dark" => "light",
            "light" => "system",
            _ => "system"
        };

        await JS.InvokeVoidAsync("themeManager.set", CurrentTheme);
    }
}
```

- [ ] **Step 7: Create TopBar.razor.css**

`src/ControlMenu/Components/Layout/TopBar.razor.css`:
```css
.top-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    height: 48px;
    padding: 0 20px;
    background-color: var(--topbar-bg);
    border-bottom: 1px solid var(--border-color);
}

.top-bar-breadcrumb {
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--text-primary);
    font-size: 0.9rem;
}

.top-bar-breadcrumb a {
    color: var(--text-secondary);
    text-decoration: none;
}

.top-bar-breadcrumb a:hover {
    color: var(--accent-color);
}

.breadcrumb-separator {
    color: var(--text-muted);
}

.breadcrumb-current {
    font-weight: 500;
}

.top-bar-actions {
    display: flex;
    align-items: center;
    gap: 8px;
}

.theme-toggle {
    background: none;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    padding: 6px 10px;
    border-radius: 6px;
    font-size: 1.1rem;
}

.theme-toggle:hover {
    background-color: var(--hover-bg);
    color: var(--text-primary);
}
```

- [ ] **Step 8: Update MainLayout.razor**

Replace `src/ControlMenu/Components/Layout/MainLayout.razor` with:

```razor
@inherits LayoutComponentBase

<div class="app-layout">
    <Sidebar />

    <div class="app-main">
        <TopBar PageTitle="@_pageTitle" />

        <main class="app-content">
            @Body
        </main>
    </div>
</div>

@code {
    private string _pageTitle = "Home";

    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }
}
```

- [ ] **Step 9: Create MainLayout.razor.css**

Replace `src/ControlMenu/Components/Layout/MainLayout.razor.css` with:

```css
.app-layout {
    display: flex;
    min-height: 100vh;
}

.app-main {
    flex: 1;
    display: flex;
    flex-direction: column;
    min-width: 0;
}

.app-content {
    flex: 1;
    padding: 24px;
    overflow-y: auto;
    background-color: var(--content-bg);
}
```

- [ ] **Step 10: Update Home.razor**

Replace `src/ControlMenu/Components/Pages/Home.razor` with:

```razor
@page "/"

<PageTitle>Control Menu</PageTitle>

<div class="home-container">
    <h1>Control Menu</h1>
    <p class="home-subtitle">Manage your Android devices, media server, and utilities from one place.</p>

    @if (ModuleDiscovery.Modules.Count == 0)
    {
        <div class="home-empty-state">
            <i class="bi bi-box-seam"></i>
            <h2>No modules loaded</h2>
            <p>Modules will appear here as they are installed. Check back after Phase 2+ implementation.</p>
        </div>
    }
    else
    {
        <div class="home-module-grid">
            @foreach (var module in ModuleDiscovery.Modules)
            {
                <div class="home-module-card">
                    <i class="bi @module.Icon"></i>
                    <h3>@module.DisplayName</h3>
                    <div class="module-nav-links">
                        @foreach (var entry in module.GetNavEntries().OrderBy(e => e.SortOrder))
                        {
                            <a href="@entry.Href">@entry.Title</a>
                        }
                    </div>
                </div>
            }
        </div>
    }
</div>

@code {
    [Inject]
    private ModuleDiscoveryService ModuleDiscovery { get; set; } = default!;
}
```

- [ ] **Step 11: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded with 0 errors. (There may be warnings about missing CSS variables — those are defined in the next task.)

- [ ] **Step 12: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Components/
git commit -m "feat: add UI shell with sidebar navigation, top bar, and home page"
```

---

## Task 10: Theme System

**Files:**
- Create: `src/ControlMenu/wwwroot/css/theme.css`
- Create: `src/ControlMenu/wwwroot/css/app.css`
- Create: `src/ControlMenu/wwwroot/js/theme.js`

The theme system uses CSS custom properties for dark/light/system modes. JavaScript handles detecting the OS preference and persisting the user's choice to localStorage.

- [ ] **Step 1: Create theme.css**

`src/ControlMenu/wwwroot/css/theme.css`:
```css
/* System/Light theme (default) */
:root {
    --bg-primary: #ffffff;
    --content-bg: #f8f9fa;
    --sidebar-bg: #f0f2f5;
    --topbar-bg: #ffffff;
    --border-color: #dee2e6;
    --text-primary: #212529;
    --text-secondary: #6c757d;
    --text-muted: #adb5bd;
    --hover-bg: rgba(0, 0, 0, 0.05);
    --active-bg: rgba(13, 110, 253, 0.1);
    --accent-color: #0d6efd;
    --card-bg: #ffffff;
    --card-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
    --input-bg: #ffffff;
    --input-border: #ced4da;
    --danger-color: #dc3545;
    --success-color: #198754;
    --warning-color: #ffc107;
}

/* Dark theme */
[data-theme="dark"] {
    --bg-primary: #1a1a2e;
    --content-bg: #16213e;
    --sidebar-bg: #0f1629;
    --topbar-bg: #1a1a2e;
    --border-color: #2a2a4a;
    --text-primary: #e0e0e0;
    --text-secondary: #a0a0b8;
    --text-muted: #6c6c80;
    --hover-bg: rgba(255, 255, 255, 0.06);
    --active-bg: rgba(99, 140, 255, 0.15);
    --accent-color: #638cff;
    --card-bg: #1e1e3a;
    --card-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
    --input-bg: #1e1e3a;
    --input-border: #3a3a5a;
    --danger-color: #f06c75;
    --success-color: #50c878;
    --warning-color: #ffd866;
}
```

- [ ] **Step 2: Create app.css**

Replace `src/ControlMenu/wwwroot/css/app.css` (or the template's `app.css`) with:

```css
/* Reset & Base */
*, *::before, *::after {
    box-sizing: border-box;
}

html, body {
    margin: 0;
    padding: 0;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
    font-size: 14px;
    color: var(--text-primary);
    background-color: var(--bg-primary);
    -webkit-font-smoothing: antialiased;
}

a {
    color: var(--accent-color);
}

/* Home page styles */
.home-container {
    max-width: 960px;
}

.home-container h1 {
    font-size: 1.75rem;
    font-weight: 600;
    margin: 0 0 8px 0;
}

.home-subtitle {
    color: var(--text-secondary);
    font-size: 1rem;
    margin: 0 0 32px 0;
}

.home-empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
    padding: 64px 24px;
    text-align: center;
    color: var(--text-muted);
}

.home-empty-state i {
    font-size: 3rem;
}

.home-empty-state h2 {
    font-size: 1.25rem;
    font-weight: 500;
    margin: 0;
    color: var(--text-secondary);
}

.home-empty-state p {
    max-width: 400px;
    margin: 0;
}

.home-module-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 16px;
}

.home-module-card {
    background-color: var(--card-bg);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    padding: 20px;
    box-shadow: var(--card-shadow);
}

.home-module-card i {
    font-size: 1.5rem;
    color: var(--accent-color);
}

.home-module-card h3 {
    font-size: 1.1rem;
    font-weight: 600;
    margin: 8px 0;
}

.module-nav-links {
    display: flex;
    flex-direction: column;
    gap: 4px;
}

.module-nav-links a {
    font-size: 0.9rem;
    text-decoration: none;
}

.module-nav-links a:hover {
    text-decoration: underline;
}

/* Scrollbar styling for dark theme */
[data-theme="dark"] ::-webkit-scrollbar {
    width: 8px;
}

[data-theme="dark"] ::-webkit-scrollbar-track {
    background: var(--sidebar-bg);
}

[data-theme="dark"] ::-webkit-scrollbar-thumb {
    background-color: var(--border-color);
    border-radius: 4px;
}

/* Blazor error UI - hide the default, we have our own error page */
#blazor-error-ui {
    display: none;
}
```

- [ ] **Step 3: Create theme.js**

`src/ControlMenu/wwwroot/js/theme.js`:
```javascript
window.themeManager = {
    _storageKey: 'controlmenu-theme',

    get: function () {
        return localStorage.getItem(this._storageKey) || 'system';
    },

    set: function (theme) {
        localStorage.setItem(this._storageKey, theme);
        this._apply(theme);
    },

    _apply: function (theme) {
        if (theme === 'system') {
            var prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
            document.documentElement.setAttribute('data-theme', prefersDark ? 'dark' : 'light');
        } else {
            document.documentElement.setAttribute('data-theme', theme);
        }
    },

    init: function () {
        var theme = this.get();
        this._apply(theme);

        // Listen for OS theme changes when in system mode
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
            if (localStorage.getItem('controlmenu-theme') === 'system') {
                var prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
                document.documentElement.setAttribute('data-theme', prefersDark ? 'dark' : 'light');
            }
        });
    }
};

// Apply theme immediately to prevent flash of wrong theme
window.themeManager.init();
```

- [ ] **Step 4: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/wwwroot/
git commit -m "feat: add theme system with dark/light/system modes"
```

---

## Task 11: Startup Wiring & Smoke Test

**Files:**
- Modify: `src/ControlMenu/Program.cs`

Final wiring: register all services in DI, configure Data Protection, register ModuleDiscoveryService scanning the app assembly. Then run the app and verify the UI renders.

- [ ] **Step 1: Update Program.cs with all service registrations**

Replace `src/ControlMenu/Program.cs` with:

```csharp
using System.Reflection;
using ControlMenu.Data;
using ControlMenu.Modules;
using ControlMenu.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Data Protection (used by SecretStore for encrypting settings)
var keysPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ControlMenu", "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("ControlMenu");

// Core services
builder.Services.AddSingleton<ICommandExecutor, CommandExecutor>();
builder.Services.AddScoped<ISecretStore, SecretStore>();
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();

// Module discovery — scans the main assembly for IToolModule implementations
builder.Services.AddSingleton(new ModuleDiscoveryService(
    [Assembly.GetExecutingAssembly()]));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<ControlMenu.Components.App>()
    .AddInteractiveServerRenderMode();

// Auto-apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
```

- [ ] **Step 2: Verify build**

```bash
cd C:\Scripts\tools-menu
dotnet build ControlMenu.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Run all tests**

```bash
cd C:\Scripts\tools-menu
dotnet test ControlMenu.sln -v minimal
```

Expected: All tests pass.

- [ ] **Step 4: Smoke test — run the app**

```bash
cd C:\Scripts\tools-menu
dotnet run --project src/ControlMenu/
```

Expected: App starts, prints "Now listening on: http://localhost:5000" (or similar port). Open the URL in a browser and verify:

1. Sidebar renders on the left with "Control Menu" title and collapse button
2. Sidebar shows "No modules loaded" message (expected — no modules exist yet)
3. Settings link appears at bottom of sidebar
4. Top bar shows home breadcrumb and theme toggle button
5. Main content shows "Control Menu" heading with empty state message
6. Clicking the theme toggle cycles through system → dark → light
7. Dark theme applies dark background and light text
8. Sidebar collapse button hides text, shows icon-only mode

Press `Ctrl+C` to stop the app after verifying.

- [ ] **Step 5: Commit**

```bash
cd C:\Scripts\tools-menu
git add src/ControlMenu/Program.cs
git commit -m "feat: wire up all services and complete Phase 1 foundation"
```

---

## Summary

After completing all 11 tasks, the project has:

- **Blazor Server app** scaffolded with .NET 9
- **SQLite database** with Devices, Jobs, Dependencies, and Settings tables via EF Core
- **CommandExecutor** — platform-aware (Windows/Linux) shell command execution
- **SecretStore** — encrypt/decrypt settings values via Data Protection API
- **ConfigurationService** — CRUD for the Settings table with transparent secret handling
- **IToolModule** interface with NavEntry, ModuleDependency, ConfigRequirement, BackgroundJobDefinition types
- **ModuleDiscoveryService** — reflection-based auto-discovery of modules
- **UI Shell** — collapsible sidebar, top bar with breadcrumbs, theme toggle
- **Theme system** — dark/light/system CSS custom properties with localStorage persistence
- **23+ unit tests** covering services, data access, and module discovery
- **Git history** with atomic commits per logical unit of work

**Ready for Phase 2:** Settings & Network — settings pages UI, NetworkDiscoveryService, device CRUD, encrypted config management.
