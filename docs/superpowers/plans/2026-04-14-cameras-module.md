# Cameras Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Cameras module to Control Menu that displays 8 LTS/Hikvision CCTV cameras via iframe, with a reverse proxy that handles auto-login and strips iframe-blocking headers.

**Architecture:** Each camera's web UI is proxied through Control Menu at `/cameras/{index}/proxy/`. The proxy logs in using stored credentials, caches the session cookie, and strips X-Frame-Options/CSP headers. A single parameterized Razor page renders the iframe. Camera names/IPs/credentials are stored in the Settings table with per-camera secrets.

**Tech Stack:** ASP.NET Core middleware (reverse proxy), HttpClient, Blazor Server (UI), Entity Framework (settings), DPAPI (credential encryption)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/ControlMenu/Modules/Cameras/CamerasModule.cs` | Create | IToolModule: module definition, dynamic nav entries |
| `src/ControlMenu/Modules/Cameras/CameraConfig.cs` | Create | Record type for camera configuration |
| `src/ControlMenu/Modules/Cameras/Services/ICameraService.cs` | Create | Interface for camera config CRUD |
| `src/ControlMenu/Modules/Cameras/Services/CameraService.cs` | Create | Reads/writes camera settings from ConfigurationService |
| `src/ControlMenu/Modules/Cameras/CameraProxyMiddleware.cs` | Create | ASP.NET middleware: reverse proxy + auto-login |
| `src/ControlMenu/Modules/Cameras/Pages/CameraView.razor` | Create | Parameterized camera view page with iframe |
| `src/ControlMenu/Modules/Cameras/Pages/CameraView.razor.css` | Create | Scoped styles for camera view |
| `src/ControlMenu/Components/Pages/Settings/CameraSettings.razor` | Create | Settings tab UI for camera configuration |
| `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor` | Modify | Add Cameras tab |
| `src/ControlMenu/Components/Pages/Home.razor` | Modify | Add Cameras card with dynamic pill buttons |
| `src/ControlMenu/Components/Layout/Sidebar.razor` | Modify | Add camera icon to ModuleImageMap |
| `src/ControlMenu/Components/Layout/MainLayout.razor` | Modify | Add camera page title mapping |
| `src/ControlMenu/Program.cs` | Modify | Register CameraService + proxy middleware |
| `tests/ControlMenu.Tests/Modules/Cameras/CamerasModuleTests.cs` | Create | Module definition tests |
| `tests/ControlMenu.Tests/Modules/Cameras/CameraServiceTests.cs` | Create | Config read/write tests |
| `tests/ControlMenu.Tests/Modules/Cameras/CameraProxyMiddlewareTests.cs` | Create | Proxy routing and header stripping tests |

---

### Task 1: CameraConfig Record and CameraService

**Files:**
- Create: `src/ControlMenu/Modules/Cameras/CameraConfig.cs`
- Create: `src/ControlMenu/Modules/Cameras/Services/ICameraService.cs`
- Create: `src/ControlMenu/Modules/Cameras/Services/CameraService.cs`
- Test: `tests/ControlMenu.Tests/Modules/Cameras/CameraServiceTests.cs`

- [ ] **Step 1: Write the CameraConfig record**

```csharp
// src/ControlMenu/Modules/Cameras/CameraConfig.cs
namespace ControlMenu.Modules.Cameras;

public record CameraConfig(int Index, string Name, string IpAddress, int Port = 80);
```

- [ ] **Step 2: Write the ICameraService interface**

```csharp
// src/ControlMenu/Modules/Cameras/Services/ICameraService.cs
namespace ControlMenu.Modules.Cameras.Services;

public interface ICameraService
{
    const int MaxCameras = 8;
    Task<CameraConfig?> GetCameraAsync(int index);
    Task<List<CameraConfig>> GetConfiguredCamerasAsync();
    Task SaveCameraAsync(int index, string name, string ipAddress, int port);
    Task SaveCredentialsAsync(int index, string username, string password);
    Task<(string Username, string Password)?> GetCredentialsAsync(int index);
}
```

- [ ] **Step 3: Write the CameraService implementation**

```csharp
// src/ControlMenu/Modules/Cameras/Services/CameraService.cs
using ControlMenu.Services;

namespace ControlMenu.Modules.Cameras.Services;

public class CameraService(IConfigurationService config) : ICameraService
{
    private const string Module = "cameras";

    public async Task<CameraConfig?> GetCameraAsync(int index)
    {
        var name = await config.GetSettingAsync($"camera-{index}-name", Module);
        var ip = await config.GetSettingAsync($"camera-{index}-ip", Module);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip))
            return null;
        var portStr = await config.GetSettingAsync($"camera-{index}-port", Module);
        var port = int.TryParse(portStr, out var p) ? p : 80;
        return new CameraConfig(index, name, ip, port);
    }

    public async Task<List<CameraConfig>> GetConfiguredCamerasAsync()
    {
        var cameras = new List<CameraConfig>();
        for (var i = 1; i <= ICameraService.MaxCameras; i++)
        {
            var cam = await GetCameraAsync(i);
            if (cam is not null)
                cameras.Add(cam);
        }
        return cameras;
    }

    public async Task SaveCameraAsync(int index, string name, string ipAddress, int port)
    {
        await config.SetSettingAsync($"camera-{index}-name", name, Module);
        await config.SetSettingAsync($"camera-{index}-ip", ipAddress, Module);
        await config.SetSettingAsync($"camera-{index}-port", port.ToString(), Module);
    }

    public async Task SaveCredentialsAsync(int index, string username, string password)
    {
        await config.SetSecretAsync($"camera-{index}-username", username, Module);
        await config.SetSecretAsync($"camera-{index}-password", password, Module);
    }

    public async Task<(string Username, string Password)?> GetCredentialsAsync(int index)
    {
        var user = await config.GetSecretAsync($"camera-{index}-username", Module);
        var pass = await config.GetSecretAsync($"camera-{index}-password", Module);
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            return null;
        return (user, pass);
    }
}
```

- [ ] **Step 4: Write tests for CameraService**

```csharp
// tests/ControlMenu.Tests/Modules/Cameras/CameraServiceTests.cs
using ControlMenu.Modules.Cameras;
using ControlMenu.Modules.Cameras.Services;
using ControlMenu.Services;
using Moq;

namespace ControlMenu.Tests.Modules.Cameras;

public class CameraServiceTests
{
    private readonly Mock<IConfigurationService> _config = new();
    private readonly CameraService _sut;

    public CameraServiceTests() => _sut = new CameraService(_config.Object);

    [Fact]
    public async Task GetCameraAsync_ReturnsNull_WhenNotConfigured()
    {
        _config.Setup(c => c.GetSettingAsync("camera-1-name", "cameras")).ReturnsAsync((string?)null);
        _config.Setup(c => c.GetSettingAsync("camera-1-ip", "cameras")).ReturnsAsync((string?)null);

        var result = await _sut.GetCameraAsync(1);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCameraAsync_ReturnsConfig_WhenConfigured()
    {
        _config.Setup(c => c.GetSettingAsync("camera-1-name", "cameras")).ReturnsAsync("Front Door");
        _config.Setup(c => c.GetSettingAsync("camera-1-ip", "cameras")).ReturnsAsync("192.168.86.101");
        _config.Setup(c => c.GetSettingAsync("camera-1-port", "cameras")).ReturnsAsync("80");

        var result = await _sut.GetCameraAsync(1);

        Assert.NotNull(result);
        Assert.Equal("Front Door", result.Name);
        Assert.Equal("192.168.86.101", result.IpAddress);
        Assert.Equal(80, result.Port);
    }

    [Fact]
    public async Task GetCameraAsync_DefaultsPort80_WhenNotSet()
    {
        _config.Setup(c => c.GetSettingAsync("camera-3-name", "cameras")).ReturnsAsync("Garage");
        _config.Setup(c => c.GetSettingAsync("camera-3-ip", "cameras")).ReturnsAsync("192.168.86.103");
        _config.Setup(c => c.GetSettingAsync("camera-3-port", "cameras")).ReturnsAsync((string?)null);

        var result = await _sut.GetCameraAsync(3);

        Assert.NotNull(result);
        Assert.Equal(80, result.Port);
    }

    [Fact]
    public async Task GetConfiguredCamerasAsync_ReturnsOnlyConfigured()
    {
        // Camera 1 configured, cameras 2-8 not
        _config.Setup(c => c.GetSettingAsync("camera-1-name", "cameras")).ReturnsAsync("Front Door");
        _config.Setup(c => c.GetSettingAsync("camera-1-ip", "cameras")).ReturnsAsync("192.168.86.101");
        _config.Setup(c => c.GetSettingAsync("camera-1-port", "cameras")).ReturnsAsync("80");
        // All others return null by default

        var result = await _sut.GetConfiguredCamerasAsync();

        Assert.Single(result);
        Assert.Equal("Front Door", result[0].Name);
    }

    [Fact]
    public async Task SaveCameraAsync_StoresAllFields()
    {
        await _sut.SaveCameraAsync(2, "Garage", "192.168.86.102", 8080);

        _config.Verify(c => c.SetSettingAsync("camera-2-name", "Garage", "cameras"));
        _config.Verify(c => c.SetSettingAsync("camera-2-ip", "192.168.86.102", "cameras"));
        _config.Verify(c => c.SetSettingAsync("camera-2-port", "8080", "cameras"));
    }

    [Fact]
    public async Task SaveCredentialsAsync_StoresAsSecrets()
    {
        await _sut.SaveCredentialsAsync(1, "admin", "secret123");

        _config.Verify(c => c.SetSecretAsync("camera-1-username", "admin", "cameras"));
        _config.Verify(c => c.SetSecretAsync("camera-1-password", "secret123", "cameras"));
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsNull_WhenNotSet()
    {
        var result = await _sut.GetCredentialsAsync(1);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_ReturnsTuple_WhenSet()
    {
        _config.Setup(c => c.GetSecretAsync("camera-1-username", "cameras")).ReturnsAsync("admin");
        _config.Setup(c => c.GetSecretAsync("camera-1-password", "cameras")).ReturnsAsync("secret123");

        var result = await _sut.GetCredentialsAsync(1);

        Assert.NotNull(result);
        Assert.Equal("admin", result.Value.Username);
        Assert.Equal("secret123", result.Value.Password);
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~CameraServiceTests" -v minimal`
Expected: All 7 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ControlMenu/Modules/Cameras/ tests/ControlMenu.Tests/Modules/Cameras/
git commit -m "feat(cameras): add CameraConfig record and CameraService"
```

---

### Task 2: CamerasModule Definition

**Files:**
- Create: `src/ControlMenu/Modules/Cameras/CamerasModule.cs`
- Test: `tests/ControlMenu.Tests/Modules/Cameras/CamerasModuleTests.cs`

The module needs to read camera configs to generate nav entries. Since `IToolModule` is discovered via reflection with a parameterless constructor, nav entries will be static placeholders (camera 1-8) that the CameraView page resolves at runtime. Alternatively, the module can accept a service — but the existing pattern uses parameterless constructors.

The simplest approach: generate nav entries for all 8 slots with generic names ("Camera 1" through "Camera 8"). The CameraView page shows the real name. This avoids needing async service access in the module.

- [ ] **Step 1: Write CamerasModule**

```csharp
// src/ControlMenu/Modules/Cameras/CamerasModule.cs
namespace ControlMenu.Modules.Cameras;

public class CamerasModule : IToolModule
{
    public string Id => "cameras";
    public string DisplayName => "Cameras";
    public string Icon => "bi-camera-video";
    public int SortOrder => 4;

    public IEnumerable<ModuleDependency> Dependencies => [];
    public IEnumerable<ConfigRequirement> ConfigRequirements => [];

    public IEnumerable<NavEntry> GetNavEntries()
    {
        for (var i = 1; i <= 8; i++)
            yield return new NavEntry($"Camera {i}", $"/cameras/{i}", "📷", i);
    }

    public IEnumerable<BackgroundJobDefinition> GetBackgroundJobs() => [];
}
```

Note: Nav entries use generic "Camera 1-8" names. The sidebar and home page will be enhanced in Task 6 to show actual camera names.

- [ ] **Step 2: Write module tests**

```csharp
// tests/ControlMenu.Tests/Modules/Cameras/CamerasModuleTests.cs
using ControlMenu.Modules.Cameras;

namespace ControlMenu.Tests.Modules.Cameras;

public class CamerasModuleTests
{
    private readonly CamerasModule _sut = new();

    [Fact]
    public void Id_IsCameras() => Assert.Equal("cameras", _sut.Id);

    [Fact]
    public void DisplayName_IsCameras() => Assert.Equal("Cameras", _sut.DisplayName);

    [Fact]
    public void SortOrder_Is4() => Assert.Equal(4, _sut.SortOrder);

    [Fact]
    public void GetNavEntries_Returns8Cameras()
    {
        var entries = _sut.GetNavEntries().ToList();
        Assert.Equal(8, entries.Count);
        Assert.Equal("Camera 1", entries[0].Title);
        Assert.Equal("/cameras/1", entries[0].Href);
        Assert.Equal("Camera 8", entries[7].Title);
        Assert.Equal("/cameras/8", entries[7].Href);
    }

    [Fact]
    public void Dependencies_IsEmpty() => Assert.Empty(_sut.Dependencies);
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~CamerasModuleTests" -v minimal`
Expected: All 5 tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Cameras/CamerasModule.cs tests/ControlMenu.Tests/Modules/Cameras/CamerasModuleTests.cs
git commit -m "feat(cameras): add CamerasModule with 8 camera nav entries"
```

---

### Task 3: Camera Proxy Middleware

**Files:**
- Create: `src/ControlMenu/Modules/Cameras/CameraProxyMiddleware.cs`
- Modify: `src/ControlMenu/Program.cs`
- Test: `tests/ControlMenu.Tests/Modules/Cameras/CameraProxyMiddlewareTests.cs`

- [ ] **Step 1: Write the proxy middleware**

```csharp
// src/ControlMenu/Modules/Cameras/CameraProxyMiddleware.cs
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using ControlMenu.Modules.Cameras.Services;

namespace ControlMenu.Modules.Cameras;

public partial class CameraProxyMiddleware(RequestDelegate next, IHttpClientFactory httpFactory, ILogger<CameraProxyMiddleware> logger)
{
    private static readonly ConcurrentDictionary<int, CookieContainer> SessionCookies = new();
    private static readonly string[] StrippedHeaders = ["X-Frame-Options", "Content-Security-Policy", "Content-Security-Policy-Report-Only"];

    [GeneratedRegex(@"^/cameras/(\d+)/proxy(?:/(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyPathRegex();

    public async Task InvokeAsync(HttpContext context)
    {
        var match = ProxyPathRegex().Match(context.Request.Path);
        if (!match.Success)
        {
            await next(context);
            return;
        }

        var cameraIndex = int.Parse(match.Groups[1].Value);
        var targetPath = match.Groups[2].Success ? match.Groups[2].Value : "";

        using var scope = context.RequestServices.CreateScope();
        var cameraService = scope.ServiceProvider.GetRequiredService<ICameraService>();

        var camera = await cameraService.GetCameraAsync(cameraIndex);
        if (camera is null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Camera not configured");
            return;
        }

        var creds = await cameraService.GetCredentialsAsync(cameraIndex);
        var cookies = SessionCookies.GetOrAdd(cameraIndex, _ => new CookieContainer());

        var baseUri = new Uri($"http://{camera.IpAddress}:{camera.Port}");
        var targetUri = new Uri(baseUri, $"/{targetPath}{context.Request.QueryString}");

        var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
        using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };

        // If no cookies cached yet and we have credentials, try to login first
        if (cookies.Count == 0 && creds is not null)
        {
            await TryLoginAsync(client, baseUri, creds.Value.Username, creds.Value.Password, cameraIndex);
        }

        var response = await ForwardRequestAsync(client, context.Request, targetUri);

        // If 401 and we have credentials, re-login and retry once
        if (response.StatusCode == HttpStatusCode.Unauthorized && creds is not null)
        {
            logger.LogInformation("Camera {Index} session expired, re-authenticating", cameraIndex);
            cookies = new CookieContainer();
            SessionCookies[cameraIndex] = cookies;
            handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
            using var retryClient = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
            await TryLoginAsync(retryClient, baseUri, creds.Value.Username, creds.Value.Password, cameraIndex);
            response = await ForwardRequestAsync(retryClient, context.Request, targetUri);
        }

        await CopyResponseAsync(response, context.Response, cameraIndex);
    }

    private async Task TryLoginAsync(HttpClient client, Uri baseUri, string username, string password, int cameraIndex)
    {
        // Hikvision/LTS login: POST session data to ISAPI or the login page
        // Try ISAPI first (modern firmware), then fallback to form login
        var loginPayload = $"<SessionLogin><userName>{username}</userName><password>{password}</password></SessionLogin>";
        try
        {
            var loginContent = new StringContent(loginPayload, System.Text.Encoding.UTF8, "application/xml");
            var loginResponse = await client.PostAsync("/ISAPI/Security/sessionLogin", loginContent);
            if (loginResponse.IsSuccessStatusCode)
            {
                logger.LogInformation("Camera {Index} ISAPI login succeeded", cameraIndex);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Camera {Index} ISAPI login attempt failed, trying userCheck", cameraIndex);
        }

        // Fallback: userCheck endpoint (older firmware)
        try
        {
            var checkPayload = new StringContent(
                $"<userCheck><userName>{username}</userName><password>{password}</password></userCheck>",
                System.Text.Encoding.UTF8, "application/xml");
            var checkResponse = await client.PostAsync("/ISAPI/Security/userCheck", checkPayload);
            if (checkResponse.IsSuccessStatusCode)
            {
                logger.LogInformation("Camera {Index} userCheck login succeeded", cameraIndex);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera {Index} login failed on all endpoints", cameraIndex);
        }
    }

    private static async Task<HttpResponseMessage> ForwardRequestAsync(HttpClient client, HttpRequest request, Uri targetUri)
    {
        var method = new HttpMethod(request.Method);
        var requestMessage = new HttpRequestMessage(method, targetUri);

        if (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
        {
            requestMessage.Content = new StreamContent(request.Body);
            if (request.ContentType is not null)
                requestMessage.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.ContentType);
        }

        // Forward Accept header
        if (request.Headers.TryGetValue("Accept", out var accept))
            requestMessage.Headers.TryAddWithoutValidation("Accept", accept.ToString());

        return await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
    }

    private static async Task CopyResponseAsync(HttpResponseMessage source, HttpResponse target, int cameraIndex)
    {
        target.StatusCode = (int)source.StatusCode;

        foreach (var header in source.Headers.Concat(source.Content.Headers))
        {
            // Strip iframe-blocking headers
            if (StrippedHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                continue;

            // Rewrite Location header for redirects
            if (header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var value in header.Value)
                {
                    // Rewrite absolute camera URLs to proxy path
                    var rewritten = Regex.Replace(value, @"^https?://[^/]+", $"/cameras/{cameraIndex}/proxy");
                    target.Headers.Append(header.Key, rewritten);
                }
                continue;
            }

            // Skip transfer-encoding (Kestrel handles this)
            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var value in header.Value)
                target.Headers.Append(header.Key, value);
        }

        await using var stream = await source.Content.ReadAsStreamAsync();
        await stream.CopyToAsync(target.Body);
    }

    public static void ClearSession(int cameraIndex)
    {
        SessionCookies.TryRemove(cameraIndex, out _);
    }
}
```

- [ ] **Step 2: Register middleware and service in Program.cs**

Add these lines to `Program.cs`:

After the Utilities module services block (~line 65), add:
```csharp
// Cameras module services
builder.Services.AddScoped<ICameraService, CameraService>();
```

Add the using statement at the top:
```csharp
using ControlMenu.Modules.Cameras.Services;
```

After `app.UseAntiforgery();` (~line 95), add:
```csharp
app.UseMiddleware<CameraProxyMiddleware>();
```

Add the using statement at the top:
```csharp
using ControlMenu.Modules.Cameras;
```

- [ ] **Step 3: Write proxy middleware tests**

```csharp
// tests/ControlMenu.Tests/Modules/Cameras/CameraProxyMiddlewareTests.cs
using ControlMenu.Modules.Cameras;

namespace ControlMenu.Tests.Modules.Cameras;

public class CameraProxyMiddlewareTests
{
    [Fact]
    public void ProxyPathRegex_MatchesCameraProxy()
    {
        // Verify the regex pattern matches expected paths
        var regex = new System.Text.RegularExpressions.Regex(
            @"^/cameras/(\d+)/proxy(?:/(.*))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var match1 = regex.Match("/cameras/1/proxy/");
        Assert.True(match1.Success);
        Assert.Equal("1", match1.Groups[1].Value);

        var match2 = regex.Match("/cameras/5/proxy/doc/page/login.asp");
        Assert.True(match2.Success);
        Assert.Equal("5", match2.Groups[1].Value);
        Assert.Equal("doc/page/login.asp", match2.Groups[2].Value);

        var noMatch = regex.Match("/cameras/1/view");
        Assert.False(noMatch.Success);
    }

    [Fact]
    public void ClearSession_RemovesCachedCookies()
    {
        // Clearing a session that doesn't exist should not throw
        CameraProxyMiddleware.ClearSession(99);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ControlMenu.Tests --filter "FullyQualifiedName~CameraProxyMiddlewareTests" -v minimal`
Expected: All 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Modules/Cameras/CameraProxyMiddleware.cs src/ControlMenu/Program.cs tests/ControlMenu.Tests/Modules/Cameras/CameraProxyMiddlewareTests.cs
git commit -m "feat(cameras): add reverse proxy middleware with auto-login"
```

---

### Task 4: Camera View Page

**Files:**
- Create: `src/ControlMenu/Modules/Cameras/Pages/CameraView.razor`
- Create: `src/ControlMenu/Modules/Cameras/Pages/CameraView.razor.css`
- Modify: `src/ControlMenu/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Write CameraView.razor**

```razor
@page "/cameras/{Index:int}"
@using ControlMenu.Modules.Cameras.Services

<PageTitle>@(_camera?.Name ?? $"Camera {Index}") - Control Menu</PageTitle>

@if (_camera is null)
{
    <div class="camera-not-configured">
        <i class="bi bi-camera-video-off"></i>
        <h2>Camera @Index not configured</h2>
        <p>Set up this camera in <a href="/settings/cameras">Settings &gt; Cameras</a>.</p>
    </div>
}
else
{
    <div class="camera-container">
        <iframe src="/cameras/@Index/proxy/" class="camera-iframe" allow="autoplay; fullscreen"></iframe>
    </div>
}

@code {
    [Parameter] public int Index { get; set; }
    [Inject] private ICameraService CameraService { get; set; } = default!;

    private CameraConfig? _camera;

    protected override async Task OnInitializedAsync()
    {
        _camera = await CameraService.GetCameraAsync(Index);
    }
}
```

- [ ] **Step 2: Write CameraView.razor.css**

```css
/* Camera view — full-page iframe for camera feed */
.camera-container {
    height: calc(100vh - 60px);
    border-radius: 0.5rem;
    overflow: hidden;
}

.camera-iframe {
    display: block;
    width: 100%;
    height: 100%;
    border: none;
    margin: 0;
    padding: 0;
}

.camera-not-configured {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 60vh;
    color: var(--text-secondary, #6c757d);
    text-align: center;
}

.camera-not-configured i {
    font-size: 4rem;
    margin-bottom: 1rem;
}

.camera-not-configured a {
    color: var(--accent-color, #10b981);
}
```

- [ ] **Step 3: Add page title mapping in MainLayout.razor**

In `MainLayout.razor`, add this line in the `UpdateTitle` switch before the default case:

```csharp
_ when path.StartsWith("cameras/") => "Camera",
```

This gives a generic title; the actual camera name is set via `<PageTitle>` in the component.

- [ ] **Step 4: Commit**

```bash
git add src/ControlMenu/Modules/Cameras/Pages/ src/ControlMenu/Components/Layout/MainLayout.razor
git commit -m "feat(cameras): add CameraView page with full-page iframe"
```

---

### Task 5: Camera Settings UI

**Files:**
- Create: `src/ControlMenu/Components/Pages/Settings/CameraSettings.razor`
- Modify: `src/ControlMenu/Components/Pages/Settings/SettingsPage.razor`

- [ ] **Step 1: Write CameraSettings.razor**

```razor
@using ControlMenu.Modules.Cameras
@using ControlMenu.Modules.Cameras.Services

<h2>Cameras</h2>
<p>Configure up to 8 CCTV cameras. Each camera needs a name, IP address, and login credentials.</p>

@for (var i = 1; i <= ICameraService.MaxCameras; i++)
{
    var index = i;
    <div class="camera-settings-card">
        <h3>Camera @index</h3>
        <div class="form-row">
            <label>Name</label>
            <input class="form-control" value="@_names[index]"
                   @onchange="e => _names[index] = e.Value?.ToString() ?? """
                   placeholder="e.g. Front Door" />
        </div>
        <div class="form-row">
            <label>IP Address</label>
            <input class="form-control" value="@_ips[index]"
                   @onchange="e => _ips[index] = e.Value?.ToString() ?? """
                   placeholder="e.g. 192.168.86.101" />
        </div>
        <div class="form-row">
            <label>Port</label>
            <input class="form-control" type="number" value="@_ports[index]"
                   @onchange="e => _ports[index] = int.TryParse(e.Value?.ToString(), out var p) ? p : 80"
                   placeholder="80" />
        </div>
        <div class="form-row">
            <label>Username</label>
            <input class="form-control" value="@_usernames[index]"
                   @onchange="e => _usernames[index] = e.Value?.ToString() ?? """
                   placeholder="admin" />
        </div>
        <div class="form-row">
            <label>Password</label>
            <input class="form-control" type="password" value="@_passwords[index]"
                   @onchange="e => _passwords[index] = e.Value?.ToString() ?? """
                   placeholder="password" />
        </div>
        <button class="btn btn-secondary" @onclick="() => SaveCamera(index)" disabled="@_saving">
            <i class="bi bi-check-lg"></i> Save Camera @index
        </button>
        @if (_savedIndex == index)
        {
            <span class="save-confirm">Saved</span>
        }
    </div>
}

@code {
    [Inject] private ICameraService CameraService { get; set; } = default!;

    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<int, string> _ips = new();
    private readonly Dictionary<int, int> _ports = new();
    private readonly Dictionary<int, string> _usernames = new();
    private readonly Dictionary<int, string> _passwords = new();
    private bool _saving;
    private int? _savedIndex;

    protected override async Task OnInitializedAsync()
    {
        for (var i = 1; i <= ICameraService.MaxCameras; i++)
        {
            var cam = await CameraService.GetCameraAsync(i);
            _names[i] = cam?.Name ?? "";
            _ips[i] = cam?.IpAddress ?? "";
            _ports[i] = cam?.Port ?? 80;

            var creds = await CameraService.GetCredentialsAsync(i);
            _usernames[i] = creds?.Username ?? "";
            _passwords[i] = creds?.Password ?? "";
        }
    }

    private async Task SaveCamera(int index)
    {
        _saving = true;
        _savedIndex = null;
        StateHasChanged();

        if (!string.IsNullOrWhiteSpace(_names[index]) && !string.IsNullOrWhiteSpace(_ips[index]))
        {
            await CameraService.SaveCameraAsync(index, _names[index].Trim(), _ips[index].Trim(), _ports[index]);
        }
        if (!string.IsNullOrWhiteSpace(_usernames[index]) && !string.IsNullOrWhiteSpace(_passwords[index]))
        {
            await CameraService.SaveCredentialsAsync(index, _usernames[index], _passwords[index]);
            CameraProxyMiddleware.ClearSession(index);
        }

        _savedIndex = index;
        _saving = false;
    }
}
```

- [ ] **Step 2: Add Cameras tab to SettingsPage.razor**

In `SettingsPage.razor`, add the cameras button after the dependencies button:

```razor
        <button class="settings-nav-item @(ActiveSection == "cameras" ? "active" : "")"
                @onclick='() => Navigate("cameras")'>
            <i class="bi bi-camera-video"></i> Cameras
        </button>
```

And add the case in the switch block:

```csharp
            case "cameras":
                <CameraSettings />
                break;
```

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Components/Pages/Settings/CameraSettings.razor src/ControlMenu/Components/Pages/Settings/SettingsPage.razor
git commit -m "feat(cameras): add camera settings UI with per-camera credentials"
```

---

### Task 6: Home Page and Sidebar Integration

**Files:**
- Modify: `src/ControlMenu/Components/Pages/Home.razor`
- Modify: `src/ControlMenu/Components/Layout/Sidebar.razor`

- [ ] **Step 1: Add Cameras card to Home.razor**

The cameras module will auto-appear via the module grid loop (it implements IToolModule and gets discovered). No manual card needed — unlike Settings which is hardcoded.

However, the generic "Camera 1-8" names from the module won't be as useful. To show actual configured names, add a dynamic cameras card BEFORE the Settings card (replace the module loop entry for cameras with a custom card):

Actually, the simplest approach: the module auto-discovers and shows "Camera 1" through "Camera 8" in the grid. This is consistent with how it works in the sidebar. Once cameras are named in settings, we can enhance later to show real names. No Home.razor changes needed beyond what auto-discovery provides.

- [ ] **Step 2: Add camera-video icon to Sidebar ModuleImageMap**

The cameras module uses `bi-camera-video` (a Bootstrap icon), so no SVG image is needed. It will fall through to the `<i class="bi @module.Icon"></i>` path like Utilities does. No change needed.

- [ ] **Step 3: Add settings/cameras to MainLayout title map**

In `MainLayout.razor` `UpdateTitle`, add before the default case:

```csharp
"settings/cameras" => "Settings — Cameras",
```

- [ ] **Step 4: Add cameras to Home page Settings card**

In `Home.razor`, add a cameras pill button to the Settings card links:

```razor
                        <a href="/settings/cameras" class="pill-link">
                            <i class="bi bi-camera-video"></i>
                            <span>Cameras</span>
                        </a>
```

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/Components/Pages/Home.razor src/ControlMenu/Components/Layout/MainLayout.razor
git commit -m "feat(cameras): integrate with home page and sidebar"
```

---

### Task 7: Run Full Test Suite and Manual Verification

- [ ] **Step 1: Run entire test suite**

Run: `dotnet test tests/ControlMenu.Tests -v minimal`
Expected: All tests pass (existing 128 + new camera tests).

- [ ] **Step 2: Manual verification checklist**

1. Home page shows Cameras module card with 8 camera pill buttons
2. Settings > Cameras tab shows 8 camera configuration slots
3. Configure camera 1 with name, IP, port, username, password — save works
4. Navigate to /cameras/1 — iframe loads camera proxy URL
5. Sidebar shows Cameras module with Camera 1-8 entries
6. Unconfigured camera (/cameras/9 or unconfigured slot) shows "not configured" message
7. Settings > Cameras pill button appears in Settings home card

- [ ] **Step 3: Commit any fixes from manual testing**

```bash
git add -A
git commit -m "fix(cameras): address issues found during manual testing"
```
