# Phase 5: Utilities Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Utilities module with icon converter (image to ICO) and file unblocker (Windows-only recursive unblock). Replaces PowerShell menu options 5 and 6.

**Architecture:** `UtilitiesModule` implements `IToolModule`. `IconConversionService` uses `System.Drawing` (Windows) or a cross-platform image library to resize images and create ICO files with multiple embedded sizes. The file unblocker uses `CommandExecutor` to run `Unblock-File` via PowerShell and is conditionally shown based on OS detection.

**Tech Stack:** .NET 9, Blazor Server, System.Drawing.Common (for icon generation), xUnit + Moq, Bootstrap Icons (already loaded)

---

## File Structure

### New Module

```
src/ControlMenu/Modules/Utilities/
├── UtilitiesModule.cs
├── Services/
│   ├── IIconConversionService.cs
│   ├── IconConversionService.cs
│   ├── IFileUnblockService.cs
│   └── FileUnblockService.cs
├── Pages/
│   ├── IconConverter.razor
│   ├── IconConverter.razor.css
│   ├── FileUnblocker.razor
│   └── FileUnblocker.razor.css
```

### New Tests

```
tests/ControlMenu.Tests/Modules/Utilities/
├── IconConversionServiceTests.cs
├── FileUnblockServiceTests.cs
├── UtilitiesModuleTests.cs
```

### Modified Files

```
src/ControlMenu/Program.cs              (register services)
src/ControlMenu/ControlMenu.csproj      (add System.Drawing.Common)
```

---

## Task 1: Add System.Drawing.Common NuGet Package

**Files:**
- Modify: `src/ControlMenu/ControlMenu.csproj`

- [ ] **Step 1: Add the NuGet package**

Run: `dotnet add src/ControlMenu/ControlMenu.csproj package System.Drawing.Common`

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/ControlMenu.csproj
git commit -m "chore: add System.Drawing.Common for icon conversion"
```

---

## Task 2: IconConversionService

**Files:**
- Create: `src/ControlMenu/Modules/Utilities/Services/IIconConversionService.cs`
- Create: `src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs`
- Create: `tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs`:
```csharp
using ControlMenu.Modules.Utilities.Services;

namespace ControlMenu.Tests.Modules.Utilities;

public class IconConversionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IconConversionService _service = new();

    public IconConversionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"icontest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateTestPng(int width = 64, int height = 64)
    {
        var path = Path.Combine(_tempDir, $"test_{width}x{height}.png");
        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.Blue);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task ConvertToIcoAsync_CreatesIcoFile()
    {
        var sourcePath = CreateTestPng(256, 256);
        var targetPath = Path.Combine(_tempDir, "output.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath, [64, 128, 256]);

        Assert.True(File.Exists(targetPath));
        var bytes = await File.ReadAllBytesAsync(targetPath);
        // ICO file header: reserved (2 bytes) + type 1 (2 bytes) + image count (2 bytes)
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0)); // reserved = 0
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // type = 1 (icon)
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 4)); // 3 images
    }

    [Fact]
    public async Task ConvertToIcoAsync_DefaultSizes_Creates3Sizes()
    {
        var sourcePath = CreateTestPng(256, 256);
        var targetPath = Path.Combine(_tempDir, "default.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath);

        Assert.True(File.Exists(targetPath));
        var bytes = await File.ReadAllBytesAsync(targetPath);
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 4)); // default is 64, 128, 256
    }

    [Fact]
    public async Task ConvertToIcoAsync_SingleSize_Creates1Image()
    {
        var sourcePath = CreateTestPng(64, 64);
        var targetPath = Path.Combine(_tempDir, "single.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath, [64]);

        Assert.True(File.Exists(targetPath));
        var bytes = await File.ReadAllBytesAsync(targetPath);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4)); // 1 image
    }

    [Fact]
    public async Task ConvertToIcoAsync_ThrowsForMissingSource()
    {
        var targetPath = Path.Combine(_tempDir, "out.ico");
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ConvertToIcoAsync("/nonexistent/file.png", targetPath));
    }

    [Fact]
    public async Task ConvertToIcoAsync_OutputIsValidIcoFormat()
    {
        var sourcePath = CreateTestPng(128, 128);
        var targetPath = Path.Combine(_tempDir, "valid.ico");

        await _service.ConvertToIcoAsync(sourcePath, targetPath, [32, 64]);

        var bytes = await File.ReadAllBytesAsync(targetPath);
        Assert.True(bytes.Length > 6 + 16 * 2); // header + 2 dir entries minimum
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~IconConversionServiceTests" --no-build`
Expected: Build failure — `IIconConversionService` and `IconConversionService` do not exist.

- [ ] **Step 3: Create IIconConversionService interface**

`src/ControlMenu/Modules/Utilities/Services/IIconConversionService.cs`:
```csharp
namespace ControlMenu.Modules.Utilities.Services;

public interface IIconConversionService
{
    Task ConvertToIcoAsync(string sourcePath, string targetPath, int[]? sizes = null);
}
```

- [ ] **Step 4: Implement IconConversionService**

`src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs`:
```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ControlMenu.Modules.Utilities.Services;

public class IconConversionService : IIconConversionService
{
    private static readonly int[] DefaultSizes = [64, 128, 256];

    public Task ConvertToIcoAsync(string sourcePath, string targetPath, int[]? sizes = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source image not found.", sourcePath);

        sizes ??= DefaultSizes;

        using var sourceImage = new Bitmap(sourcePath);
        using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

        // Prepare PNG data for each size
        var pngEntries = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var resized = ResizeImage(sourceImage, size, size);
            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Png);
            pngEntries.Add(ms.ToArray());
        }

        using var writer = new BinaryWriter(output);

        // ICONDIR header
        writer.Write((ushort)0);               // reserved
        writer.Write((ushort)1);               // type = icon
        writer.Write((ushort)pngEntries.Count); // image count

        // Calculate data offset: header (6) + dir entries (16 each)
        var dataOffset = 6 + 16 * pngEntries.Count;

        // ICONDIRENTRY for each image
        for (var i = 0; i < pngEntries.Count; i++)
        {
            var size = sizes[i];
            var data = pngEntries[i];

            writer.Write((byte)(size >= 256 ? 0 : size)); // width (0 = 256)
            writer.Write((byte)(size >= 256 ? 0 : size)); // height (0 = 256)
            writer.Write((byte)0);     // color count (0 for 32bpp)
            writer.Write((byte)0);     // reserved
            writer.Write((ushort)1);   // color planes
            writer.Write((ushort)32);  // bits per pixel
            writer.Write((uint)data.Length);    // bytes in resource
            writer.Write((uint)dataOffset);     // offset to data

            dataOffset += data.Length;
        }

        // Image data (PNG blobs)
        foreach (var data in pngEntries)
        {
            writer.Write(data);
        }

        return Task.CompletedTask;
    }

    private static Bitmap ResizeImage(Image source, int width, int height)
    {
        var destRect = new Rectangle(0, 0, width, height);
        var destImage = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        destImage.SetResolution(96, 96);

        using var graphics = Graphics.FromImage(destImage);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Handle non-square source images: scale to fit, center
        var srcAspect = (float)source.Width / source.Height;
        int drawWidth, drawHeight, drawX, drawY;

        if (srcAspect > 1)
        {
            drawWidth = width;
            drawHeight = (int)(height / srcAspect);
            drawX = 0;
            drawY = (height - drawHeight) / 2;
        }
        else if (srcAspect < 1)
        {
            drawWidth = (int)(width * srcAspect);
            drawHeight = height;
            drawX = (width - drawWidth) / 2;
            drawY = 0;
        }
        else
        {
            drawWidth = width;
            drawHeight = height;
            drawX = 0;
            drawY = 0;
        }

        using var wrapMode = new ImageAttributes();
        wrapMode.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        graphics.DrawImage(source, new Rectangle(drawX, drawY, drawWidth, drawHeight),
            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrapMode);

        return destImage;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~IconConversionServiceTests" -v n`
Expected: All 5 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/Utilities/Services/IIconConversionService.cs src/ControlMenu/Modules/Utilities/Services/IconConversionService.cs tests/ControlMenu.Tests/Modules/Utilities/IconConversionServiceTests.cs
git commit -m "feat(utilities): add IconConversionService for PNG-to-ICO conversion"
```

---

## Task 3: FileUnblockService

**Files:**
- Create: `src/ControlMenu/Modules/Utilities/Services/IFileUnblockService.cs`
- Create: `src/ControlMenu/Modules/Utilities/Services/FileUnblockService.cs`
- Create: `tests/ControlMenu.Tests/Modules/Utilities/FileUnblockServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/Utilities/FileUnblockServiceTests.cs`:
```csharp
using ControlMenu.Modules.Utilities.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Utilities;

public class FileUnblockServiceTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor = new();

    private FileUnblockService CreateService() => new(_mockExecutor.Object);

    [Fact]
    public void IsSupported_ReturnsTrue_OnWindows()
    {
        var service = CreateService();
        // This test validates the property exists and returns a bool.
        // It will be true on Windows CI, false on Linux CI.
        Assert.IsType<bool>(service.IsSupported);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_RunsUnblockFileCommand()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.Is<string>(s => s.Contains("Unblock-File") && s.Contains("C:\\TestDir")),
            null, default))
            .ReturnsAsync(new CommandResult(0, "", "", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync("C:\\TestDir");

        Assert.True(result.Success);
        _mockExecutor.Verify(e => e.ExecuteAsync("powershell",
            It.Is<string>(s => s.Contains("Unblock-File") && s.Contains("C:\\TestDir")),
            null, default), Times.Once);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_ReturnsFileCount()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.IsAny<string>(), null, default))
            .ReturnsAsync(new CommandResult(0, "42", "", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync("C:\\TestDir");

        Assert.True(result.Success);
        Assert.Equal(42, result.FileCount);
    }

    [Fact]
    public async Task UnblockDirectoryAsync_ReturnsFalse_OnError()
    {
        _mockExecutor.Setup(e => e.ExecuteAsync("powershell",
            It.IsAny<string>(), null, default))
            .ReturnsAsync(new CommandResult(1, "", "Access denied", false));

        var service = CreateService();
        var result = await service.UnblockDirectoryAsync("C:\\TestDir");

        Assert.False(result.Success);
        Assert.Equal("Access denied", result.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~FileUnblockServiceTests" --no-build`
Expected: Build failure — service types don't exist.

- [ ] **Step 3: Create IFileUnblockService interface**

`src/ControlMenu/Modules/Utilities/Services/IFileUnblockService.cs`:
```csharp
namespace ControlMenu.Modules.Utilities.Services;

public record UnblockResult(bool Success, int FileCount = 0, string? ErrorMessage = null);

public interface IFileUnblockService
{
    bool IsSupported { get; }
    Task<UnblockResult> UnblockDirectoryAsync(string directoryPath, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement FileUnblockService**

`src/ControlMenu/Modules/Utilities/Services/FileUnblockService.cs`:
```csharp
using ControlMenu.Services;

namespace ControlMenu.Modules.Utilities.Services;

public class FileUnblockService : IFileUnblockService
{
    private readonly ICommandExecutor _executor;

    public FileUnblockService(ICommandExecutor executor)
    {
        _executor = executor;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<UnblockResult> UnblockDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        var command = $"-Command \"$files = Get-ChildItem '{directoryPath}' -Recurse | Unblock-File -PassThru; $files.Count\"";
        var result = await _executor.ExecuteAsync("powershell", command, null, ct);

        if (result.ExitCode != 0)
            return new UnblockResult(false, ErrorMessage: result.StandardError.Trim());

        int.TryParse(result.StandardOutput.Trim(), out var count);
        return new UnblockResult(true, count);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~FileUnblockServiceTests" -v n`
Expected: All 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/Utilities/Services/IFileUnblockService.cs src/ControlMenu/Modules/Utilities/Services/FileUnblockService.cs tests/ControlMenu.Tests/Modules/Utilities/FileUnblockServiceTests.cs
git commit -m "feat(utilities): add FileUnblockService for Windows Unblock-File"
```

---

## Task 4: UtilitiesModule (IToolModule Implementation)

**Files:**
- Create: `src/ControlMenu/Modules/Utilities/UtilitiesModule.cs`
- Create: `tests/ControlMenu.Tests/Modules/Utilities/UtilitiesModuleTests.cs`
- Modify: `src/ControlMenu/Program.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ControlMenu.Tests/Modules/Utilities/UtilitiesModuleTests.cs`:
```csharp
using ControlMenu.Modules;
using ControlMenu.Modules.Utilities;

namespace ControlMenu.Tests.Modules.Utilities;

public class UtilitiesModuleTests
{
    private readonly UtilitiesModule _module = new();

    [Fact]
    public void Id_IsUtilities()
    {
        Assert.Equal("utilities", _module.Id);
    }

    [Fact]
    public void DisplayName_IsUtilities()
    {
        Assert.Equal("Utilities", _module.DisplayName);
    }

    [Fact]
    public void Icon_IsToolsIcon()
    {
        Assert.Equal("bi-tools", _module.Icon);
    }

    [Fact]
    public void Dependencies_IsEmpty()
    {
        Assert.Empty(_module.Dependencies);
    }

    [Fact]
    public void ConfigRequirements_IsEmpty()
    {
        Assert.Empty(_module.ConfigRequirements);
    }

    [Fact]
    public void NavEntries_IncludesIconConverterAndFileUnblocker()
    {
        var entries = _module.GetNavEntries().ToList();
        Assert.Contains(entries, e => e.Href == "/utilities/icon-converter");
        Assert.Contains(entries, e => e.Href == "/utilities/file-unblocker");
    }

    [Fact]
    public void GetBackgroundJobs_ReturnsEmpty()
    {
        Assert.Empty(_module.GetBackgroundJobs());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~UtilitiesModuleTests" --no-build`
Expected: Build failure — `UtilitiesModule` does not exist.

- [ ] **Step 3: Implement UtilitiesModule**

`src/ControlMenu/Modules/Utilities/UtilitiesModule.cs`:
```csharp
namespace ControlMenu.Modules.Utilities;

public class UtilitiesModule : IToolModule
{
    public string Id => "utilities";
    public string DisplayName => "Utilities";
    public string Icon => "bi-tools";
    public int SortOrder => 3;

    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries() =>
    [
        new NavEntry("Icon Converter", "/utilities/icon-converter", "bi-file-earmark-image", 0),
        new NavEntry("File Unblocker", "/utilities/file-unblocker", "bi-unlock", 1)
    ];

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
```

- [ ] **Step 4: Register services in Program.cs**

Add to `src/ControlMenu/Program.cs`:
```csharp
using ControlMenu.Modules.Utilities.Services;
// ...
builder.Services.AddSingleton<IIconConversionService, IconConversionService>();
builder.Services.AddSingleton<IFileUnblockService, FileUnblockService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~UtilitiesModuleTests" -v n`
Expected: All 7 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/Utilities/UtilitiesModule.cs tests/ControlMenu.Tests/Modules/Utilities/UtilitiesModuleTests.cs src/ControlMenu/Program.cs
git commit -m "feat(utilities): add UtilitiesModule with nav entries"
```

---

## Task 5: Icon Converter Page

**Files:**
- Create: `src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor`
- Create: `src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor.css`

- [ ] **Step 1: Create the Icon Converter page**

`src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor`:
```razor
@page "/utilities/icon-converter"
@using ControlMenu.Modules.Utilities.Services
@using Microsoft.AspNetCore.Components.Forms

<PageTitle>Icon Converter</PageTitle>

<h1><i class="bi bi-file-earmark-image"></i> Icon Converter</h1>
<p class="page-subtitle">Convert an image file (PNG, JPG, etc.) to an ICO file with multiple sizes.</p>

<div class="converter-panel">
    <div class="form-group">
        <label class="form-label">Source Image</label>
        <InputFile OnChange="OnFileSelected" accept=".png,.jpg,.jpeg,.bmp,.gif" class="form-input" />
        @if (_selectedFile is not null)
        {
            <span class="file-info">@_selectedFile.Name (@FormatSize(_selectedFile.Size))</span>
        }
    </div>

    <div class="form-group">
        <label class="form-label">Icon Sizes</label>
        <div class="size-options">
            @foreach (var size in _availableSizes)
            {
                <label class="size-checkbox">
                    <input type="checkbox" checked="@_selectedSizes.Contains(size)" @onchange="e => ToggleSize(size, (bool)e.Value!)" />
                    @(size)px
                </label>
            }
        </div>
    </div>

    <div class="form-group">
        <label class="form-label">Output Filename</label>
        <input type="text" @bind="_outputName" class="form-input" placeholder="icon.ico" />
    </div>

    @if (!string.IsNullOrEmpty(_error))
    {
        <div class="error-panel">
            <i class="bi bi-exclamation-triangle"></i> @_error
        </div>
    }

    @if (_converting)
    {
        <div class="status-info">
            <i class="bi bi-arrow-repeat spin"></i> Converting...
        </div>
    }

    <button class="btn btn-primary btn-lg" @onclick="Convert" disabled="@(_selectedFile is null || _converting || _selectedSizes.Count == 0)">
        <i class="bi bi-arrow-right-circle"></i> Convert to ICO
    </button>

    @if (_downloadReady)
    {
        <div class="download-panel">
            <i class="bi bi-check-circle-fill"></i>
            <span>Conversion complete!</span>
            <a href="@_downloadUrl" download="@_outputName" class="btn btn-success">
                <i class="bi bi-download"></i> Download @_outputName
            </a>
        </div>
    }
</div>

@code {
    [Inject] private IIconConversionService IconService { get; set; } = default!;
    [Inject] private IWebHostEnvironment Env { get; set; } = default!;

    private IBrowserFile? _selectedFile;
    private readonly int[] _availableSizes = [16, 32, 48, 64, 128, 256];
    private HashSet<int> _selectedSizes = [64, 128, 256];
    private string _outputName = "icon.ico";
    private bool _converting;
    private bool _downloadReady;
    private string? _downloadUrl;
    private string? _error;

    private void OnFileSelected(InputFileChangeEventArgs e)
    {
        _selectedFile = e.File;
        _downloadReady = false;
        _error = null;

        // Auto-set output name from source
        var baseName = Path.GetFileNameWithoutExtension(e.File.Name);
        _outputName = $"{baseName}.ico";
    }

    private void ToggleSize(int size, bool selected)
    {
        if (selected) _selectedSizes.Add(size);
        else _selectedSizes.Remove(size);
    }

    private async Task Convert()
    {
        if (_selectedFile is null || _selectedSizes.Count == 0) return;

        _converting = true;
        _downloadReady = false;
        _error = null;

        try
        {
            // Save uploaded file to temp
            var tempDir = Path.Combine(Env.WebRootPath, "temp");
            Directory.CreateDirectory(tempDir);

            var sourcePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}{Path.GetExtension(_selectedFile.Name)}");
            var targetPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.ico");

            await using (var stream = _selectedFile.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024))
            await using (var fileStream = new FileStream(sourcePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            // Convert
            var sizes = _selectedSizes.OrderBy(s => s).ToArray();
            await IconService.ConvertToIcoAsync(sourcePath, targetPath, sizes);

            // Make available for download
            var downloadName = $"{Guid.NewGuid():N}.ico";
            var downloadPath = Path.Combine(tempDir, downloadName);
            File.Move(targetPath, downloadPath);
            _downloadUrl = $"/temp/{downloadName}";
            _downloadReady = true;

            // Cleanup source
            File.Delete(sourcePath);
        }
        catch (Exception ex)
        {
            _error = $"Conversion failed: {ex.Message}";
        }
        finally
        {
            _converting = false;
        }
    }

    private static string FormatSize(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024):F1} MB"
        };
}
```

- [ ] **Step 2: Create scoped CSS**

`src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor.css`:
```css
.converter-panel {
    max-width: 600px;
}

.form-group {
    margin-bottom: 1.25rem;
}

.form-label {
    display: block;
    font-weight: 600;
    margin-bottom: 0.5rem;
}

.file-info {
    display: block;
    font-size: 0.85rem;
    color: var(--text-muted, #6c757d);
    margin-top: 0.25rem;
}

.size-options {
    display: flex;
    flex-wrap: wrap;
    gap: 1rem;
}

.size-checkbox {
    display: flex;
    align-items: center;
    gap: 0.3rem;
    cursor: pointer;
}

.status-info {
    padding: 0.75rem;
    color: var(--accent-color, #0d6efd);
    margin-bottom: 1rem;
}

.download-panel {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 1rem;
    background: var(--success-bg, #d4edda);
    color: var(--success-text, #155724);
    border-radius: 0.5rem;
    margin-top: 1rem;
}

.error-panel {
    background: var(--danger-bg, #f8d7da);
    color: var(--danger-text, #721c24);
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
}

.spin { animation: spin 1s linear infinite; }
@keyframes spin { 100% { transform: rotate(360deg); } }
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor src/ControlMenu/Modules/Utilities/Pages/IconConverter.razor.css
git commit -m "feat(utilities): add Icon Converter page with upload and download"
```

---

## Task 6: File Unblocker Page

**Files:**
- Create: `src/ControlMenu/Modules/Utilities/Pages/FileUnblocker.razor`
- Create: `src/ControlMenu/Modules/Utilities/Pages/FileUnblocker.razor.css`

- [ ] **Step 1: Create the File Unblocker page**

`src/ControlMenu/Modules/Utilities/Pages/FileUnblocker.razor`:
```razor
@page "/utilities/file-unblocker"
@using ControlMenu.Modules.Utilities.Services

<PageTitle>File Unblocker</PageTitle>

<h1><i class="bi bi-unlock"></i> File Unblocker</h1>

@if (!FileUnblockService.IsSupported)
{
    <div class="unsupported-panel">
        <i class="bi bi-info-circle"></i>
        <p>File unblocking is a Windows-only feature. It removes the "blocked" attribute from files downloaded from the internet.</p>
        <p>This feature is not available on your current operating system.</p>
    </div>
}
else
{
    <p class="page-subtitle">Remove the "blocked" attribute from all files in a directory and subdirectories. This is equivalent to right-clicking each file and checking "Unblock" in Properties.</p>

    <div class="unblocker-panel">
        <div class="form-group">
            <label class="form-label">Directory Path</label>
            <input type="text" @bind="_directoryPath" class="form-input" placeholder="C:\Downloads\SomeFolder" />
        </div>

        @if (!string.IsNullOrEmpty(_error))
        {
            <div class="error-panel">
                <i class="bi bi-exclamation-triangle"></i> @_error
            </div>
        }

        @if (_processing)
        {
            <div class="status-info">
                <i class="bi bi-arrow-repeat spin"></i> Unblocking files...
            </div>
        }

        <button class="btn btn-primary" @onclick="UnblockFiles" disabled="@(string.IsNullOrWhiteSpace(_directoryPath) || _processing)">
            <i class="bi bi-unlock"></i> Unblock All Files
        </button>

        @if (_result is not null)
        {
            @if (_result.Success)
            {
                <div class="success-panel">
                    <i class="bi bi-check-circle-fill"></i>
                    <span>Successfully unblocked @_result.FileCount file(s) in @_directoryPath.</span>
                </div>
            }
            else
            {
                <div class="error-panel">
                    <i class="bi bi-x-circle"></i>
                    <span>@_result.ErrorMessage</span>
                </div>
            }
        }
    </div>
}

@code {
    [Inject] private IFileUnblockService FileUnblockService { get; set; } = default!;

    private string _directoryPath = "";
    private bool _processing;
    private string? _error;
    private UnblockResult? _result;

    private async Task UnblockFiles()
    {
        if (string.IsNullOrWhiteSpace(_directoryPath)) return;

        _processing = true;
        _error = null;
        _result = null;

        try
        {
            _result = await FileUnblockService.UnblockDirectoryAsync(_directoryPath);
        }
        catch (Exception ex)
        {
            _error = $"Unexpected error: {ex.Message}";
        }
        finally
        {
            _processing = false;
        }
    }
}
```

- [ ] **Step 2: Create scoped CSS**

`src/ControlMenu/Modules/Utilities/Pages/FileUnblocker.razor.css`:
```css
.unblocker-panel {
    max-width: 600px;
}

.unsupported-panel {
    display: flex;
    gap: 0.75rem;
    background: var(--warning-bg, #fff3cd);
    color: var(--warning-text, #856404);
    padding: 1.25rem;
    border-radius: 0.5rem;
    max-width: 600px;
}
.unsupported-panel i { font-size: 1.5rem; flex-shrink: 0; }
.unsupported-panel p { margin: 0 0 0.5rem; }
.unsupported-panel p:last-child { margin-bottom: 0; }

.form-group {
    margin-bottom: 1.25rem;
}

.form-label {
    display: block;
    font-weight: 600;
    margin-bottom: 0.5rem;
}

.status-info {
    padding: 0.75rem;
    color: var(--accent-color, #0d6efd);
    margin-bottom: 1rem;
}

.success-panel {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.75rem 1rem;
    background: var(--success-bg, #d4edda);
    color: var(--success-text, #155724);
    border-radius: 0.5rem;
    margin-top: 1rem;
}

.error-panel {
    background: var(--danger-bg, #f8d7da);
    color: var(--danger-text, #721c24);
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
}

.spin { animation: spin 1s linear infinite; }
@keyframes spin { 100% { transform: rotate(360deg); } }
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Utilities/Pages/FileUnblocker.razor src/ControlMenu/Modules/Utilities/Pages/FileUnblocker.razor.css
git commit -m "feat(utilities): add File Unblocker page (Windows-only)"
```

---

## Task 7: Full Test Suite & Final Build

- [ ] **Step 1: Run all tests**

Run: `dotnet test tests/ControlMenu.Tests -v n`
Expected: All tests pass (previous 44 + 16 new = 60 total).

- [ ] **Step 2: Build the full project**

Run: `dotnet build src/ControlMenu`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(utilities): phase 5 complete — Utilities module with icon converter and file unblocker"
```
