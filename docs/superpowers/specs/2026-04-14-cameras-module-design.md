# Cameras Module Design

## Overview

A new Control Menu module for viewing 8 LTS/Hikvision CCTV cameras via iframe embedding. Each camera's web UI is served through a reverse proxy that handles auto-login and strips iframe-blocking headers.

## Problem

LTS (Hikvision-manufactured) cameras have a form-based login page that sets session cookies via an API call. Browsers block cross-origin JavaScript access to iframe content, making it impossible to auto-fill credentials from the parent page. The cameras also set `X-Frame-Options: SAMEORIGIN`, blocking direct iframe embedding.

## Solution: Reverse Proxy with Credential Injection

Control Menu proxies all camera traffic through `localhost:5159`, making it same-origin. The proxy handles login automatically using stored credentials and strips iframe-blocking headers from responses.

## Module Structure

```
Modules/Cameras/
    CamerasModule.cs          # IToolModule implementation
    CameraConfig.cs           # Camera record (Id, Name, IpAddress, Port)
    Services/
        CameraProxyService.cs # ASP.NET middleware: reverse proxy + auto-login
    Pages/
        CameraView.razor      # Single parameterized page for all cameras
        CameraView.razor.css  # Scoped styles
```

## Module Definition (CamerasModule.cs)

- **Id:** `"cameras"`
- **DisplayName:** "Cameras"
- **Icon:** `bi-camera-video`
- **SortOrder:** 4 (after Utilities)
- **Dependencies:** None
- **Nav Entries:** 8 entries, one per configured camera, dynamically generated from settings
  - Each entry: camera name, href `/cameras/{index}`, camera emoji icon
  - Cameras without a configured name/IP are omitted from nav

## Camera Configuration

### CameraConfig Record

```csharp
public record CameraConfig(int Index, string Name, string IpAddress, int Port = 80);
```

### Settings Storage

All settings scoped to `moduleId = "cameras"`:

| Key | Type | Example |
|-----|------|---------|
| `camera-{index}-name` | Setting | "Front Door" |
| `camera-{index}-ip` | Setting | "192.168.86.101" |
| `camera-{index}-port` | Setting | "80" |
| `camera-{index}-username` | Secret | "admin" |
| `camera-{index}-password` | Secret | "password123" |

Index is 1-8. Each camera has its own credentials so any camera can diverge independently.

### Settings UI

New "Cameras" tab in the Settings page (`CameraSettingsSection.razor`). Displays 8 camera slots, each with:
- Name (text input)
- IP address (text input)
- Port (text input, default 80)
- Username (text input)
- Password (password input)

Empty slots are valid (camera simply doesn't appear in nav or home page).

## Reverse Proxy (CameraProxyService.cs)

### Registration

Registered as ASP.NET middleware in `Program.cs`, handling all requests matching `/cameras/{index}/proxy/{**path}`.

### Request Flow

1. Extract camera index from URL path
2. Look up camera IP, port, and credentials from settings
3. If no active session cookie for this camera, perform login:
   a. POST to camera's login endpoint with credentials
   b. Capture session cookie from response
   c. Cache cookie in memory (keyed by camera index)
4. Forward the browser's request to `http://{cameraIp}:{port}/{path}`
   - Attach cached session cookie
   - Forward query string, request body, and relevant headers
5. Return camera's response to browser with modifications:
   - Strip `X-Frame-Options` header
   - Strip `Content-Security-Policy` header
   - Rewrite `Location` headers (redirects) to proxy path
   - Rewrite `Set-Cookie` domain/path to match proxy
6. If camera returns 401 (session expired), clear cached cookie, re-login, retry once

### Session Management

- Session cookies cached in a `ConcurrentDictionary<int, string>` (camera index to cookie value)
- No persistence needed — cookies re-acquired on app restart
- Login endpoint: likely `/ISAPI/Security/userCheck` or `/doc/page/login.asp` (to be confirmed against actual camera during implementation)

### Header Handling

**Request headers forwarded:** Host (rewritten to camera IP), Accept, Content-Type, Content-Length, Referer (rewritten)

**Response headers stripped:** X-Frame-Options, Content-Security-Policy, Content-Security-Policy-Report-Only

**Response headers rewritten:** Location (redirect URLs rewritten to proxy path), Set-Cookie (domain/path adjusted)

## Camera View Page (CameraView.razor)

### Routes

```
@page "/cameras/{Index:int}"
```

### Layout

- Page title: camera name (from settings)
- Full remaining area: iframe pointing to `/cameras/{Index}/proxy/`
- CSS follows the same pattern as GoogleTvDashboard mirror panel:
  - Grid layout with full-height iframe
  - `border-radius: 0.5rem`, `overflow: hidden`
  - iframe: `width: 100%; height: 100%; border: none`

### Error States

- Camera not configured (no IP): show "Camera not configured" message with link to Settings > Cameras
- Proxy unreachable: camera's web UI handles its own errors within the iframe

## Home Page Integration

A "Cameras" card appears on the home page (in the module grid alongside Android Devices, Jellyfin, Utilities, Settings). Contains pill buttons for each configured camera, using the camera name and a camera emoji.

## Sidebar Integration

- Module header: `bi-camera-video` icon + "Cameras" label
- Expanded: one nav entry per configured camera with camera emoji
- Follows existing localStorage persistence for expand/collapse state

## Testing

- `CamerasModuleTests.cs`: module definition, nav entry generation from settings
- `CameraProxyServiceTests.cs`: URL rewriting, header stripping, login flow, session caching, 401 retry
- Manual testing: verify iframe loads camera live view, auto-login works, session recovery on expiry

## Security Considerations

- Camera credentials stored encrypted via SecretStore (DPAPI)
- Proxy only accessible on localhost (same as Control Menu itself)
- No camera credentials exposed to browser — proxy injects them server-side
- Session cookies scoped to the proxy, not sent to the browser

## Future Considerations

- Multi-camera grid view (2x2 or 4x4 layout showing multiple cameras simultaneously)
- PTZ controls via ISAPI overlay
- Motion event log integration
- Snapshot/recording triggers
