using System.Text.RegularExpressions;
using ControlMenu.Data;
using ControlMenu.Data.Entities;
using ControlMenu.Data.Enums;
using ControlMenu.Modules;
using ControlMenu.Modules.Cameras.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlMenu.Services;

public class DependencyManagerService : IDependencyManagerService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IReadOnlyList<IToolModule> _modules;
    private readonly ICommandExecutor _executor;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfigurationService _config;
    private readonly WsScrcpyService _wsScrcpy;
    private readonly IGo2RtcService _go2Rtc;
    private readonly ILogger<DependencyManagerService> _logger;

    public DependencyManagerService(
        IDbContextFactory<AppDbContext> dbFactory,
        IReadOnlyList<IToolModule> modules,
        ICommandExecutor executor,
        IHttpClientFactory httpFactory,
        IConfigurationService config,
        WsScrcpyService wsScrcpy,
        IGo2RtcService go2Rtc,
        ILogger<DependencyManagerService> logger)
    {
        _dbFactory = dbFactory;
        _modules = modules;
        _executor = executor;
        _httpFactory = httpFactory;
        _config = config;
        _wsScrcpy = wsScrcpy;
        _go2Rtc = go2Rtc;
        _logger = logger;
    }

    public async Task SyncDependenciesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var declared = _modules
            .SelectMany(m => m.Dependencies.Select(d => (Module: m, Dep: d)))
            .ToList();

        var existing = await db.Dependencies.ToListAsync();

        // Remove orphaned
        var declaredKeys = declared
            .Select(d => (d.Module.Id, d.Dep.Name))
            .ToHashSet();

        var toRemove = existing
            .Where(e => !declaredKeys.Contains((e.ModuleId, e.Name)))
            .ToList();

        db.Dependencies.RemoveRange(toRemove);

        // Upsert declared
        foreach (var (module, dep) in declared)
        {
            var entity = existing.FirstOrDefault(e =>
                e.ModuleId == module.Id && e.Name == dep.Name);

            if (entity is null)
            {
                entity = new Dependency
                {
                    Id = Guid.NewGuid(),
                    ModuleId = module.Id,
                    Name = dep.Name,
                    Status = DependencyStatus.UpToDate
                };
                db.Dependencies.Add(entity);
            }

            // Update static fields from code
            entity.SourceType = dep.SourceType;
            entity.ProjectHomeUrl = dep.ProjectHomeUrl;
            entity.DownloadUrl = entity.DownloadUrl ?? dep.DownloadUrl;

            // Refresh installed version — check local install path first, then system PATH
            entity.InstalledVersion = await GetInstalledVersionAsync(dep, module.Id);
        }

        await db.SaveChangesAsync();
    }

    public async Task<int> GetUpdateAvailableCountAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Dependencies
            .CountAsync(d => d.Status == DependencyStatus.UpdateAvailable);
    }

    public async Task<IReadOnlyList<Dependency>> GetAllDependenciesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Dependencies
            .OrderBy(d => d.ModuleId)
            .ThenBy(d => d.Name)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<DependencyCheckResult> CheckDependencyAsync(Guid dependencyId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Dependencies.FindAsync(dependencyId);
        if (entity is null)
            return new DependencyCheckResult(dependencyId, "", DependencyStatus.CheckFailed,
                null, null, "Dependency not found");

        var moduleDep = FindModuleDependency(entity.ModuleId, entity.Name);
        if (moduleDep is null)
            return new DependencyCheckResult(dependencyId, entity.Name, DependencyStatus.CheckFailed,
                entity.InstalledVersion, null, "Module dependency declaration not found");

        // External-mode ws-scrcpy-web: health = HTTP ping of configured URL.
        // No install-path or version check makes sense when the node server lives elsewhere.
        if (entity.Name == "ws-scrcpy-web" &&
            await _wsScrcpy.GetDeployModeAsync() == WsScrcpyDeployMode.External)
        {
            var url = (await _config.GetSettingAsync("wsscrcpy-url")) ?? "http://localhost:8000";
            try
            {
                var http = _httpFactory.CreateClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var resp = await http.GetAsync(url, cts.Token);
                entity.Status = resp.IsSuccessStatusCode ? DependencyStatus.UpToDate : DependencyStatus.CheckFailed;
                entity.LastChecked = DateTime.UtcNow;
                entity.InstalledVersion = resp.IsSuccessStatusCode ? "external" : null;
                entity.LatestKnownVersion = null;
                await db.SaveChangesAsync();
                return new DependencyCheckResult(
                    entity.Id, entity.Name, entity.Status,
                    entity.InstalledVersion, entity.LatestKnownVersion,
                    resp.IsSuccessStatusCode ? null : $"ws-scrcpy-web at {url} returned HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                entity.Status = DependencyStatus.CheckFailed;
                entity.LastChecked = DateTime.UtcNow;
                entity.InstalledVersion = null;
                await db.SaveChangesAsync();
                _logger.LogWarning(ex, "ws-scrcpy-web external URL unreachable: {Url}", url);
                return new DependencyCheckResult(
                    entity.Id, entity.Name, DependencyStatus.CheckFailed,
                    null, null, $"ws-scrcpy-web at {url} unreachable: {ex.Message}");
            }
        }

        try
        {
            // Refresh installed version — check local install path first, then system PATH
            entity.InstalledVersion = await GetInstalledVersionAsync(moduleDep, entity.ModuleId);

            switch (entity.SourceType)
            {
                case UpdateSourceType.GitHub:
                    await CheckGitHubVersionAsync(entity, moduleDep);
                    break;
                case UpdateSourceType.DirectUrl:
                    await CheckDirectUrlVersionAsync(entity, moduleDep);
                    break;
                case UpdateSourceType.Manual:
                    entity.Status = DependencyStatus.UpToDate;
                    break;
            }

            entity.LastChecked = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return new DependencyCheckResult(
                entity.Id, entity.Name, entity.Status,
                entity.InstalledVersion, entity.LatestKnownVersion, null);
        }
        catch (Exception ex)
        {
            entity.Status = DependencyStatus.CheckFailed;
            entity.LastChecked = DateTime.UtcNow;
            await db.SaveChangesAsync();

            _logger.LogError(ex, "Failed to check dependency {Name}", entity.Name);
            return new DependencyCheckResult(
                entity.Id, entity.Name, DependencyStatus.CheckFailed,
                entity.InstalledVersion, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<DependencyCheckResult>> CheckAllAsync()
    {
        List<Guid> depIds;
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            depIds = await db.Dependencies.Select(d => d.Id).ToListAsync();
        }
        var results = new List<DependencyCheckResult>();

        foreach (var id in depIds)
        {
            results.Add(await CheckDependencyAsync(id));
        }

        return results;
    }

    public async Task<AssetMatch?> ResolveDownloadAssetAsync(Guid dependencyId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Dependencies.FindAsync(dependencyId);
        if (entity is null) return null;

        var moduleDep = FindModuleDependency(entity.ModuleId, entity.Name);
        if (moduleDep is null || moduleDep.InstallPath is null) return null;

        if (entity.SourceType == UpdateSourceType.GitHub && moduleDep.GitHubRepo is not null)
        {
            return await ResolveGitHubAssetAsync(moduleDep);
        }

        if (entity.SourceType == UpdateSourceType.DirectUrl && entity.DownloadUrl is not null)
        {
            // DirectUrl — deterministic URL, just need the file size
            var client = _httpFactory.CreateClient("dependency-updates");
            using var headResponse = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, entity.DownloadUrl));

            var size = headResponse.Content.Headers.ContentLength ?? 0;
            var fileName = Path.GetFileName(new Uri(entity.DownloadUrl).AbsolutePath);

            return new AssetMatch(fileName, entity.DownloadUrl, size, AutoSelected: true);
        }

        return null;
    }

    public async Task<UpdateResult> DownloadAndInstallAsync(Guid dependencyId, AssetMatch asset)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Dependencies.FindAsync(dependencyId);
        if (entity is null)
            return new UpdateResult(false, null, "Dependency not found", null);

        var moduleDep = FindModuleDependency(entity.ModuleId, entity.Name);
        if (moduleDep?.InstallPath is null)
            return new UpdateResult(false, null, "No install path configured", null);

        // Use user-configured path if set, otherwise module default
        var installPath = await GetInstallPathAsync(entity.Name, entity.ModuleId) ?? moduleDep.InstallPath;

        StaleUrlAction? urlAction = null;
        var needsAdbKill = entity.Name == "adb";
        var needsGo2RtcStop = entity.Name == "go2rtc";
        var stoppedScrcpy = false;
        var stoppedGo2Rtc = false;
        var tempDir = Path.Combine(Path.GetTempPath(), "ControlMenu", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Download
            var client = _httpFactory.CreateClient("dependency-updates");
            var response = await client.GetAsync(asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead);

            // Check if we were redirected (HttpClient follows redirects automatically)
            if (response.RequestMessage?.RequestUri?.ToString() is string finalUrl
                && finalUrl != asset.DownloadUrl)
            {
                entity.DownloadUrl = finalUrl;
                urlAction = StaleUrlAction.Redirected;
            }

            if (!response.IsSuccessStatusCode)
            {
                entity.Status = DependencyStatus.UrlInvalid;
                await db.SaveChangesAsync();
                return new UpdateResult(false, null,
                    $"Download failed: HTTP {(int)response.StatusCode}", StaleUrlAction.Invalid);
            }

            // Download to temp
            var tempFile = Path.Combine(tempDir, asset.FileName);
            await using (var fs = File.Create(tempFile))
            {
                await response.Content.CopyToAsync(fs);
            }

            // 2. Extract
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

            // 3. Verify — find the executable in extracted dir and run version command
            var newExe = FindExecutable(extractDir, moduleDep.ExecutableName);
            if (newExe is null)
                return new UpdateResult(false, null,
                    $"Could not find {moduleDep.ExecutableName} in extracted archive", urlAction);

            var versionParts = moduleDep.VersionCommand.Split(' ', 2);
            var verifyArgs = versionParts.Length > 1 ? versionParts[1] : null;
            var verifyResult = await _executor.ExecuteAsync(newExe, verifyArgs);
            if (verifyResult.ExitCode != 0)
                return new UpdateResult(false, null,
                    $"New binary verification failed: {verifyResult.StandardError}", urlAction);

            var newVersion = ExtractVersion(verifyResult.StandardOutput, moduleDep.VersionPattern);

            // 4. Swap — stop processes that lock the binary, backup old, move in new
            if (needsAdbKill)
            {
                _logger.LogInformation("Stopping ws-scrcpy-web and ADB server before updating adb");
                await _wsScrcpy.StopAsync(CancellationToken.None);
                await _executor.ExecuteAsync("adb", "kill-server");
                stoppedScrcpy = true;
                await Task.Delay(500);
            }

            if (needsGo2RtcStop && _go2Rtc.IsRunning)
            {
                _logger.LogInformation("Stopping go2rtc before updating");
                await _go2Rtc.StopAsync();
                stoppedGo2Rtc = true;
                await Task.Delay(500);
            }

            Directory.CreateDirectory(installPath);

            // Backup old files if upgrading
            foreach (var file in GetManagedFiles(moduleDep))
            {
                var fullPath = Path.Combine(installPath, file);
                if (File.Exists(fullPath))
                    File.Move(fullPath, fullPath + ".bak", overwrite: true);
            }

            // Copy new files — find the subdirectory in extracted (e.g., platform-tools/)
            var sourceDir = FindInstallSource(extractDir, moduleDep.ExecutableName);
            if (sourceDir is not null)
            {
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    File.Copy(file, Path.Combine(installPath, Path.GetFileName(file)),
                        overwrite: true);
                }
            }

            // 5. Update DB
            entity.InstalledVersion = newVersion;
            entity.LatestKnownVersion = newVersion;
            entity.Status = DependencyStatus.UpToDate;
            entity.LastChecked = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return new UpdateResult(true, newVersion, null, urlAction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install update for {Name}", entity.Name);
            return new UpdateResult(false, null, ex.Message, urlAction);
        }
        finally
        {
            if (stoppedScrcpy)
            {
                _logger.LogInformation("Restarting ws-scrcpy-web after ADB update");
                _wsScrcpy.Restart();
            }
            if (stoppedGo2Rtc)
            {
                _logger.LogInformation("Restarting go2rtc after update");
                _go2Rtc.Restart();
            }
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    public async Task<IReadOnlyList<DependencyScanResult>> ScanForDependenciesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.Dependencies.ToListAsync();
        var results = new List<DependencyScanResult>();

        foreach (var module in _modules)
        {
            foreach (var dep in module.Dependencies)
            {
                var entity = existing.FirstOrDefault(e =>
                    e.ModuleId == module.Id && e.Name == dep.Name);

                // Already configured in DB with a known version
                if (entity?.InstalledVersion is not null)
                {
                    results.Add(new DependencyScanResult(
                        dep.Name, module.Id, Found: true,
                        Path: null, Version: entity.InstalledVersion,
                        Source: "Previously configured"));
                    continue;
                }

                // Try PATH
                var pathResult = await TryScanPathAsync(dep, module.Id);
                if (pathResult is not null)
                {
                    results.Add(pathResult);
                    continue;
                }

                // Try common locations
                var locationResult = await TryScanCommonLocationsAsync(dep, module.Id);
                if (locationResult is not null)
                {
                    results.Add(locationResult);
                    continue;
                }

                // Not found anywhere
                results.Add(new DependencyScanResult(
                    dep.Name, module.Id, Found: false,
                    Path: null, Version: null,
                    Source: "Not found"));
            }
        }

        return results;
    }

    public async Task<DependencyScanResult?> ValidateManualPathAsync(string name, string moduleId, string executablePath)
    {
        if (!File.Exists(executablePath))
            return null;

        var dep = _modules
            .Where(m => m.Id == moduleId)
            .SelectMany(m => m.Dependencies)
            .FirstOrDefault(d => d.Name == name);
        if (dep is null) return null;

        // Run the version command using the manual path as the executable
        var parts = dep.VersionCommand.Split(' ', 2);
        var args = parts.Length > 1 ? parts[1] : null;

        try
        {
            var result = await _executor.ExecuteAsync(executablePath, args);
            if (result.ExitCode != 0) return null;

            var version = ExtractVersion(result.StandardOutput, dep.VersionPattern);
            if (version is null) return null;

            // Persist to DB so future scans find it as "Previously configured"
            using var db = await _dbFactory.CreateDbContextAsync();
            var entity = await db.Dependencies.FirstOrDefaultAsync(e =>
                e.ModuleId == moduleId && e.Name == name);
            if (entity is not null)
            {
                entity.InstalledVersion = version;
                await db.SaveChangesAsync();
            }

            return new DependencyScanResult(name, moduleId, Found: true,
                Path: executablePath, Version: version, Source: "Manual");
        }
        catch
        {
            return null;
        }
    }

    // --- Private helpers ---

    private async Task<DependencyScanResult?> TryScanPathAsync(ModuleDependency dep, string moduleId)
    {
        var parts = dep.VersionCommand.Split(' ', 2);
        var command = parts[0];
        var args = parts.Length > 1 ? parts[1] : null;

        try
        {
            var result = await _executor.ExecuteAsync(command, args);
            if (result.ExitCode != 0) return null;

            var version = ExtractVersion(result.StandardOutput, dep.VersionPattern);
            if (version is null) return null;

            return new DependencyScanResult(
                dep.Name, moduleId, Found: true,
                Path: command, Version: version,
                Source: "PATH");
        }
        catch
        {
            return null;
        }
    }

    private async Task<DependencyScanResult?> TryScanCommonLocationsAsync(ModuleDependency dep, string moduleId)
    {
        var exeName = dep.ExecutableName;
        if (OperatingSystem.IsWindows() && !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exeName += ".exe";

        foreach (var location in GetCommonLocations(exeName))
        {
            if (!File.Exists(location)) continue;

            try
            {
                var parts = dep.VersionCommand.Split(' ', 2);
                var args = parts.Length > 1 ? parts[1] : null;

                var result = await _executor.ExecuteAsync(location, args);
                if (result.ExitCode != 0) continue;

                var version = ExtractVersion(result.StandardOutput, dep.VersionPattern);
                if (version is null) continue;

                return new DependencyScanResult(
                    dep.Name, moduleId, Found: true,
                    Path: location, Version: version,
                    Source: location);
            }
            catch
            {
                // Continue to next location
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCommonLocations(string executableName)
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return $@"C:\platform-tools\{executableName}";
            yield return $@"C:\scrcpy\{executableName}";
            yield return $@"C:\Program Files\Android\platform-tools\{executableName}";
            yield return Path.Combine(localAppData, "Android", "Sdk", "platform-tools", executableName);
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return $"/usr/local/bin/{executableName}";
            yield return $"/opt/platform-tools/{executableName}";
            yield return $"/opt/scrcpy/{executableName}";
            yield return $"/snap/bin/{executableName}";
        }
    }

    private async Task<string?> GetInstalledVersionAsync(ModuleDependency dep, string? moduleId = null)
    {
        var parts = dep.VersionCommand.Split(' ', 2);
        var args = parts.Length > 1 ? parts[1] : null;

        // If we manage this dependency locally, only check the local path — never
        // fall back to system PATH, which would report a stale version and cause
        // an update loop.
        if (moduleId is not null && dep.InstallPath is not null)
        {
            var customPath = await _config.GetSettingAsync($"dep-path-{dep.Name}");
            var installDir = !string.IsNullOrEmpty(customPath) ? customPath : dep.InstallPath;

            var exeName = dep.ExecutableName;
            if (OperatingSystem.IsWindows() && !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                exeName += ".exe";

            var localExe = Path.Combine(installDir, exeName);
            if (!File.Exists(localExe))
                return null; // Not installed yet — don't check system PATH

            try
            {
                var localResult = await _executor.ExecuteAsync(localExe, args);
                if (localResult.ExitCode == 0)
                {
                    var v = ExtractVersion(localResult.StandardOutput, dep.VersionPattern);
                    if (v is not null) return v;
                }
            }
            catch { /* binary exists but failed to run */ }

            return null;
        }

        // No local install path — use system PATH
        var command = parts[0];
        try
        {
            var result = await _executor.ExecuteAsync(command, args);
            if (result.ExitCode != 0) return null;
            return ExtractVersion(result.StandardOutput, dep.VersionPattern);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractVersion(string output, string pattern)
    {
        var match = Regex.Match(output, pattern);
        if (!match.Success) return null;

        // If multiple capture groups (e.g., major.minor.micro), join them
        if (match.Groups.Count > 2)
        {
            return string.Join(".",
                Enumerable.Range(1, match.Groups.Count - 1)
                    .Select(i => match.Groups[i].Value));
        }

        return match.Groups[1].Value;
    }

    private async Task CheckGitHubVersionAsync(Dependency entity, ModuleDependency moduleDep)
    {
        if (moduleDep.GitHubRepo is null) return;

        using var doc = await FetchGitHubReleaseAsync(moduleDep.GitHubRepo);
        if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return;

        var tag = tagProp.GetString()?.TrimStart('v');
        if (tag is null) return;

        entity.LatestKnownVersion = tag;
        entity.Status = CompareVersions(entity.InstalledVersion, entity.LatestKnownVersion) < 0
            ? DependencyStatus.UpdateAvailable
            : DependencyStatus.UpToDate;
    }

    private async Task CheckDirectUrlVersionAsync(Dependency entity, ModuleDependency moduleDep)
    {
        if (moduleDep.VersionCheckUrl is null || moduleDep.VersionCheckPattern is null) return;

        var client = _httpFactory.CreateClient("dependency-updates");
        var content = await client.GetStringAsync(moduleDep.VersionCheckUrl);

        var match = Regex.Match(content, moduleDep.VersionCheckPattern, RegexOptions.Singleline);
        if (!match.Success) return;

        string latestVersion;
        if (match.Groups.Count > 2)
            latestVersion = string.Join(".",
                Enumerable.Range(1, match.Groups.Count - 1).Select(i => match.Groups[i].Value));
        else
            latestVersion = match.Groups[1].Value;

        entity.LatestKnownVersion = latestVersion;

        entity.Status = CompareVersions(entity.InstalledVersion, latestVersion) < 0
            ? DependencyStatus.UpdateAvailable
            : DependencyStatus.UpToDate;

        // Resolve versioned download URL from template (e.g., Node.js dist)
        if (moduleDep.DownloadUrlTemplate is not null)
        {
            entity.DownloadUrl = moduleDep.DownloadUrlTemplate.Replace("{version}", latestVersion);
        }
        // Update download URL if the current one contains an old version-encoded filename
        else if (entity.Status == DependencyStatus.UpdateAvailable && moduleDep.DownloadUrl is not null)
        {
            var updatedUrl = BuildVersionedDownloadUrl(moduleDep.DownloadUrl, content, moduleDep);
            if (updatedUrl is not null)
                entity.DownloadUrl = updatedUrl;
        }
    }

    private static string? BuildVersionedDownloadUrl(string templateUrl, string pageContent, ModuleDependency dep)
    {
        // Find the actual download URL on the page that matches the asset pattern or executable name
        var platform = OperatingSystem.IsWindows() ? "win" : "linux";
        var pattern = $@"(sqlite-tools-{platform}-x64-\d+\.zip)";
        var match = Regex.Match(pageContent, pattern);
        if (!match.Success) return null;

        var filename = match.Groups[1].Value;
        // Reconstruct the full URL based on the page's year directory
        var yearMatch = Regex.Match(pageContent, @"href=""(\d{4})/" + Regex.Escape(filename));
        if (yearMatch.Success)
            return $"https://sqlite.org/{yearMatch.Groups[1].Value}/{filename}";

        // Fall back: replace the version-encoded part of the template URL
        var templateFilename = Path.GetFileName(new Uri(templateUrl).AbsolutePath);
        var templateDir = templateUrl[..templateUrl.LastIndexOf('/')];
        return $"{templateDir}/{filename}";
    }

    private async Task<AssetMatch?> ResolveGitHubAssetAsync(ModuleDependency moduleDep)
    {
        using var doc = await FetchGitHubReleaseAsync(moduleDep.GitHubRepo!);

        var basePattern = moduleDep.AssetPattern ?? moduleDep.ExecutableName;
        var platformToken = GetPlatformToken();

        var matches = new List<AssetMatch>();
        if (doc.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                var size = asset.GetProperty("size").GetInt64();
                if (name is null || url is null) continue;

                if (Regex.IsMatch(name, basePattern) && name.Contains(platformToken))
                {
                    matches.Add(new AssetMatch(name, url, size, AutoSelected: true));
                }
            }
        }

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
            return matches[0] with { AutoSelected = false };

        return null;
    }

    private async Task<System.Text.Json.JsonDocument> FetchGitHubReleaseAsync(string repo)
    {
        var client = _httpFactory.CreateClient("github-api");
        var url = $"https://api.github.com/repos/{repo}/releases/latest";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("User-Agent", "ControlMenu");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        return await System.Text.Json.JsonDocument.ParseAsync(stream);
    }

    private static string GetPlatformToken()
    {
        if (OperatingSystem.IsWindows())
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                   System.Runtime.InteropServices.Architecture.X64 ? "win64" : "win32";
        if (OperatingSystem.IsLinux())
            return "linux-x86_64";
        return "unknown";
    }

    private ModuleDependency? FindModuleDependency(string moduleId, string name)
    {
        return _modules
            .FirstOrDefault(m => m.Id == moduleId)
            ?.Dependencies
            .FirstOrDefault(d => d.Name == name);
    }

    public bool CanAutoInstall(string name, string moduleId)
    {
        var dep = FindModuleDependency(moduleId, name);
        return dep?.InstallPath is not null && dep.SourceType != UpdateSourceType.Manual;
    }

    public string? GetInstallPath(string name, string moduleId)
    {
        // Check for user-configured override first (sync read — cached by ConfigurationService)
        return FindModuleDependency(moduleId, name)?.InstallPath;
    }

    public async Task<string?> GetInstallPathAsync(string name, string moduleId)
    {
        var custom = await _config.GetSettingAsync($"dep-path-{name}");
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        return FindModuleDependency(moduleId, name)?.InstallPath;
    }

    public async Task SetInstallPathAsync(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            await _config.DeleteSettingAsync($"dep-path-{name}");
        else
            await _config.SetSettingAsync($"dep-path-{name}", path);
    }

    public bool IsConfigurable(string name, string moduleId)
    {
        var dep = FindModuleDependency(moduleId, name);
        return dep?.InstallPath is not null;
    }

    private static int CompareVersions(string? installed, string? latest)
    {
        if (installed is null || latest is null) return -1;

        var iParts = installed.Split('.').Select(s => int.TryParse(s, out var v) ? v : 0).ToArray();
        var lParts = latest.Split('.').Select(s => int.TryParse(s, out var v) ? v : 0).ToArray();
        var len = Math.Max(iParts.Length, lParts.Length);

        for (var i = 0; i < len; i++)
        {
            var a = i < iParts.Length ? iParts[i] : 0;
            var b = i < lParts.Length ? lParts[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        return 0;
    }

    private static string? FindExecutable(string dir, string exeName)
    {
        var exe = OperatingSystem.IsWindows() && !exeName.EndsWith(".exe")
            ? exeName + ".exe" : exeName;
        return Directory.EnumerateFiles(dir, exe, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string? FindInstallSource(string extractDir, string exeName)
    {
        var exe = FindExecutable(extractDir, exeName);
        return exe is not null ? Path.GetDirectoryName(exe) : null;
    }

    private static IEnumerable<string> GetManagedFiles(ModuleDependency dep)
    {
        var exe = OperatingSystem.IsWindows() && !dep.ExecutableName.EndsWith(".exe")
            ? dep.ExecutableName + ".exe" : dep.ExecutableName;
        return [exe, .. dep.RelatedFiles];
    }
}
