# Iframe Theme Bridge — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sync ws-scrcpy-web's iframe theme with Control Menu's current theme via a cross-origin postMessage bridge.

**Architecture:** ws-scrcpy-web exposes a public `themeEmbed` module (`getTheme`/`setTheme`/`installThemeEmbedListener`/`notifyThemeReady`) shipped in v0.2.0. Control Menu adds a `scrcpyThemeBridge.js` interop that responds to the iframe's `theme-ready` handshake and pushes updates on every CM theme toggle. Two-message protocol, namespaced `ws-scrcpy-web:` prefix.

**Tech Stack:** TypeScript + Vitest (ws-scrcpy-web), vanilla JS + Blazor Server (Control Menu), webpack UMD bundle.

**Spec:** `docs/superpowers/specs/2026-04-29-iframe-theme-bridge-design.md`

**Repos:**
- `C:\Users\jscha\source\repos\ws-scrcpy-web` — Tasks 1–9 (ship v0.2.0 first)
- `C:\Users\jscha\source\repos\control-menu` — Tasks 10–14 (consumes v0.2.0)

**Branch base for ws-scrcpy-web:** Commit directly on `feature/v0.1.24` at the v0.1.24-beta.3 tip. Theme-bridge ships as part of the v0.1.24 line (Option B from 2026-04-29 brainstorm). No separate v0.2.0 release.

**Commit conventions (both repos):** Conventional commits, LF line endings, no AI attribution.

---

## Task 1: Scaffold `themeEmbed.ts` with shared `getTheme`/`setTheme`

**Repo:** `ws-scrcpy-web`

**Files:**
- Create: `src/app/public/themeEmbed.ts`
- Test: `src/app/public/__tests__/themeEmbed.test.ts`

- [ ] **Step 1: Write the failing test**

Create `src/app/public/__tests__/themeEmbed.test.ts`:

```typescript
// @vitest-environment jsdom

import { beforeEach, describe, expect, it } from 'vitest';
import { getTheme, setTheme } from '../themeEmbed';

describe('getTheme / setTheme', () => {
    beforeEach(() => {
        localStorage.clear();
        document.documentElement.removeAttribute('data-theme');
    });

    it('returns "dark" by default when localStorage is empty', () => {
        expect(getTheme()).toBe('dark');
    });

    it('setTheme("light") writes localStorage and DOM attribute', () => {
        setTheme('light');
        expect(localStorage.getItem('ws-scrcpy-web-theme')).toBe('light');
        expect(document.documentElement.getAttribute('data-theme')).toBe('light');
        expect(getTheme()).toBe('light');
    });

    it('setTheme("dark") round-trips', () => {
        setTheme('light');
        setTheme('dark');
        expect(getTheme()).toBe('dark');
        expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd C:/Users/jscha/source/repos/ws-scrcpy-web && npx vitest run src/app/public/__tests__/themeEmbed.test.ts`
Expected: FAIL with module-not-found / cannot import `../themeEmbed`.

- [ ] **Step 3: Write minimal implementation**

Create `src/app/public/themeEmbed.ts`:

```typescript
/**
 * Public theme-embed helpers for ws-scrcpy-web.
 *
 * Exposes the same get/set semantics used internally by ThemeToggle, plus
 * postMessage helpers so a parent window (e.g., a host page embedding
 * ws-scrcpy-web in an iframe) can push theme changes across origins.
 */

const STORAGE_KEY = 'ws-scrcpy-web-theme';

export type Theme = 'dark' | 'light';

export interface ThemeEmbedOptions {
    /** Default 'ws-scrcpy-web:theme'. */
    messageType?: string;
    /**
     * Origins allowed to push theme messages. Default '*' — accepts any
     * origin. WARNING: leave as '*' only when ws-scrcpy-web is intended to be
     * embeddable by arbitrary hosts. Pass an explicit allowlist
     * (e.g., ['https://my-host.example']) for locked-down deployments.
     */
    allowedOrigins?: '*' | string[];
}

export function getTheme(): Theme {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'light' ? 'light' : 'dark';
}

export function setTheme(theme: Theme): void {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem(STORAGE_KEY, theme);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/app/public/__tests__/themeEmbed.test.ts`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/app/public/themeEmbed.ts src/app/public/__tests__/themeEmbed.test.ts
git commit -m "feat(theme): scaffold themeEmbed module with get/set helpers"
```

---

## Task 2: Add `installThemeEmbedListener` with validation

**Repo:** `ws-scrcpy-web`

**Files:**
- Modify: `src/app/public/themeEmbed.ts`
- Modify: `src/app/public/__tests__/themeEmbed.test.ts`

- [ ] **Step 1: Write failing tests**

Append to `src/app/public/__tests__/themeEmbed.test.ts`:

```typescript
import { installThemeEmbedListener } from '../themeEmbed';

describe('installThemeEmbedListener', () => {
    beforeEach(() => {
        localStorage.clear();
        document.documentElement.removeAttribute('data-theme');
    });

    function postFromOrigin(origin: string, data: unknown): void {
        const evt = new MessageEvent('message', {
            data,
            origin,
            source: window,
        });
        window.dispatchEvent(evt);
    }

    it('applies a valid theme message of the default type', () => {
        const dispose = installThemeEmbedListener();
        postFromOrigin('https://example.com', { type: 'ws-scrcpy-web:theme', theme: 'light' });
        expect(getTheme()).toBe('light');
        dispose();
    });

    it('ignores wrong message type', () => {
        const dispose = installThemeEmbedListener();
        postFromOrigin('https://example.com', { type: 'other:theme', theme: 'light' });
        expect(getTheme()).toBe('dark');
        dispose();
    });

    it('ignores invalid theme values', () => {
        const dispose = installThemeEmbedListener();
        postFromOrigin('https://example.com', { type: 'ws-scrcpy-web:theme', theme: 'midnight' });
        postFromOrigin('https://example.com', { type: 'ws-scrcpy-web:theme', theme: null });
        expect(getTheme()).toBe('dark');
        dispose();
    });

    it('honors allowedOrigins allowlist', () => {
        const dispose = installThemeEmbedListener({ allowedOrigins: ['https://allowed.example'] });
        postFromOrigin('https://blocked.example', { type: 'ws-scrcpy-web:theme', theme: 'light' });
        expect(getTheme()).toBe('dark');
        postFromOrigin('https://allowed.example', { type: 'ws-scrcpy-web:theme', theme: 'light' });
        expect(getTheme()).toBe('light');
        dispose();
    });

    it('honors custom messageType', () => {
        const dispose = installThemeEmbedListener({ messageType: 'custom:theme' });
        postFromOrigin('https://example.com', { type: 'custom:theme', theme: 'light' });
        expect(getTheme()).toBe('light');
        dispose();
    });

    it('disposer detaches the listener', () => {
        const dispose = installThemeEmbedListener();
        dispose();
        postFromOrigin('https://example.com', { type: 'ws-scrcpy-web:theme', theme: 'light' });
        expect(getTheme()).toBe('dark');
    });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/app/public/__tests__/themeEmbed.test.ts`
Expected: 6 new tests FAIL (`installThemeEmbedListener` not exported).

- [ ] **Step 3: Implement `installThemeEmbedListener`**

Append to `src/app/public/themeEmbed.ts`:

```typescript
const DEFAULT_MESSAGE_TYPE = 'ws-scrcpy-web:theme';

function isTheme(value: unknown): value is Theme {
    return value === 'dark' || value === 'light';
}

export function installThemeEmbedListener(opts: ThemeEmbedOptions = {}): () => void {
    const messageType = opts.messageType ?? DEFAULT_MESSAGE_TYPE;
    const allowedOrigins = opts.allowedOrigins ?? '*';

    const handler = (event: MessageEvent): void => {
        if (allowedOrigins !== '*' && !allowedOrigins.includes(event.origin)) {
            return;
        }
        const data = event.data;
        if (!data || typeof data !== 'object') return;
        if ((data as { type?: unknown }).type !== messageType) return;
        const theme = (data as { theme?: unknown }).theme;
        if (!isTheme(theme)) return;
        setTheme(theme);
    };

    window.addEventListener('message', handler);
    return () => window.removeEventListener('message', handler);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/app/public/__tests__/themeEmbed.test.ts`
Expected: PASS, 9 tests total.

- [ ] **Step 5: Commit**

```bash
git add src/app/public/themeEmbed.ts src/app/public/__tests__/themeEmbed.test.ts
git commit -m "feat(theme): add installThemeEmbedListener with origin/type/value validation"
```

---

## Task 3: Add `notifyThemeReady`

**Repo:** `ws-scrcpy-web`

**Files:**
- Modify: `src/app/public/themeEmbed.ts`
- Modify: `src/app/public/__tests__/themeEmbed.test.ts`

- [ ] **Step 1: Add `vi` to vitest imports**

Edit `src/app/public/__tests__/themeEmbed.test.ts`. Replace the top vitest import line with:

```typescript
import { beforeEach, describe, expect, it, vi } from 'vitest';
```

- [ ] **Step 2: Write failing tests**

Append to `src/app/public/__tests__/themeEmbed.test.ts`:

```typescript
import { notifyThemeReady } from '../themeEmbed';

describe('notifyThemeReady', () => {
    beforeEach(() => {
        localStorage.clear();
        document.documentElement.removeAttribute('data-theme');
    });

    it('posts {type, theme} to the given target', () => {
        const target = { postMessage: vi.fn() } as unknown as Window;
        setTheme('light');
        notifyThemeReady(target);
        expect(target.postMessage).toHaveBeenCalledWith(
            { type: 'ws-scrcpy-web:theme-ready', theme: 'light' },
            '*',
        );
    });

    it('defaults target to window.parent', () => {
        const parentMock = { postMessage: vi.fn() };
        const originalParent = window.parent;
        Object.defineProperty(window, 'parent', { value: parentMock, configurable: true });
        try {
            notifyThemeReady();
            expect(parentMock.postMessage).toHaveBeenCalled();
        } finally {
            Object.defineProperty(window, 'parent', { value: originalParent, configurable: true });
        }
    });

    it('is a no-op when target equals window (not embedded)', () => {
        const spy = vi.spyOn(window, 'postMessage');
        notifyThemeReady(window);
        expect(spy).not.toHaveBeenCalled();
        spy.mockRestore();
    });

    it('honors custom messageType (suffixed with -ready)', () => {
        const target = { postMessage: vi.fn() } as unknown as Window;
        notifyThemeReady(target, { messageType: 'custom:theme' });
        expect(target.postMessage).toHaveBeenCalledWith(
            expect.objectContaining({ type: 'custom:theme-ready' }),
            '*',
        );
    });
});
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `npx vitest run src/app/public/__tests__/themeEmbed.test.ts`
Expected: 4 new tests FAIL.

- [ ] **Step 4: Implement `notifyThemeReady`**

Append to `src/app/public/themeEmbed.ts`:

```typescript
export function notifyThemeReady(target?: Window, opts: ThemeEmbedOptions = {}): void {
    const dest = target ?? window.parent;
    if (!dest || dest === window) return;
    const baseType = opts.messageType ?? DEFAULT_MESSAGE_TYPE;
    const readyType = `${baseType}-ready`;
    dest.postMessage({ type: readyType, theme: getTheme() }, '*');
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx vitest run src/app/public/__tests__/themeEmbed.test.ts`
Expected: PASS, 13 tests total.

- [ ] **Step 6: Commit**

```bash
git add src/app/public/themeEmbed.ts src/app/public/__tests__/themeEmbed.test.ts
git commit -m "feat(theme): add notifyThemeReady iframe→parent handshake helper"
```

---

## Task 4: Refactor `ThemeToggle.ts` to share `getTheme`/`setTheme`

**Repo:** `ws-scrcpy-web`

**Files:**
- Modify: `src/app/client/ThemeToggle.ts`

This is a pure refactor — replace the file-local helpers with imports from `themeEmbed`. Single source of truth; no behavior change. Use targeted Edits, not a full file rewrite.

- [ ] **Step 1: Read the current file to confirm line numbers**

Read `src/app/client/ThemeToggle.ts`. Confirm the structure: lines 1 (`STORAGE_KEY`), 6–8 (`getTheme`), 10–13 (`setTheme`), and that `initTheme` + `createThemeToggle` use them.

- [ ] **Step 2: Edit — replace `STORAGE_KEY` const + local helpers with an import**

Use the Edit tool. Replace the block starting at `const STORAGE_KEY = 'ws-scrcpy-web-theme';` and ending at the closing brace of the file-local `setTheme`:

```typescript
const STORAGE_KEY = 'ws-scrcpy-web-theme';

const MOON_SVG = ...;
const SUN_SVG = ...;

function getTheme(): string {
    return localStorage.getItem(STORAGE_KEY) || 'dark';
}

function setTheme(theme: string): void {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem(STORAGE_KEY, theme);
}
```

with (preserving the SVG constants as-is — they keep their existing literal values):

```typescript
import { getTheme, setTheme } from '../public/themeEmbed';

const MOON_SVG = ...;
const SUN_SVG = ...;
```

(Use Edit's `old_string` to capture the exact `STORAGE_KEY` line + the two function blocks; don't touch the SVG consts or the `initTheme`/`createThemeToggle` functions below.)

- [ ] **Step 3: Run the full test suite**

Run: `npx vitest run`
Expected: All tests pass; no regressions.

- [ ] **Step 4: Manual smoke test in dev**

Run: `npm run dev` (check `package.json` for the actual script name).
Open `http://localhost:8000`. Click the theme toggle. Confirm theme switches and persists across reload.

- [ ] **Step 5: Commit**

```bash
git add src/app/client/ThemeToggle.ts
git commit -m "refactor(theme): ThemeToggle imports get/set from public/themeEmbed"
```

---

## Task 5: Wire up listener and handshake in `index.ts`

**Repo:** `ws-scrcpy-web`

**Files:**
- Modify: `src/app/index.ts`

- [ ] **Step 1: Add import**

Edit `src/app/index.ts`. After line 10 (the existing `import { createThemeToggle, initTheme } from './client/ThemeToggle';`), add:

```typescript
import { installThemeEmbedListener, notifyThemeReady } from './public/themeEmbed';
```

- [ ] **Step 2: Add listener install + ready handshake after the `initTheme()` call (line 158)**

Find:

```typescript
initTheme();
```

Replace with:

```typescript
initTheme();
installThemeEmbedListener();
notifyThemeReady();
```

- [ ] **Step 3: Build and confirm no regressions**

Run: `npm run build`
Expected: Build succeeds, no TypeScript errors.

Run: `npx vitest run`
Expected: All tests pass.

- [ ] **Step 4: Manual smoke test in dev (standalone, not embedded)**

Run: `npm run dev`. Confirm app starts cleanly, theme still works, no console errors. The `notifyThemeReady()` call is a no-op when not embedded (target === window).

- [ ] **Step 5: Commit**

```bash
git add src/app/index.ts
git commit -m "feat(theme): install embed listener + post theme-ready handshake on load"
```

---

## Task 6: Add public re-exports

**Repo:** `ws-scrcpy-web`

**Files:**
- Modify: `src/app/public/index.ts`

- [ ] **Step 1: Add exports**

Edit `src/app/public/index.ts`. After the existing `export type { ... }` line, add:

```typescript
export {
    getTheme,
    setTheme,
    installThemeEmbedListener,
    notifyThemeReady,
} from './themeEmbed';
export type { Theme, ThemeEmbedOptions } from './themeEmbed';
```

- [ ] **Step 2: Build and check the UMD bundle**

Run: `npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Verify symbols land on `window.WsScrcpy` (manual)**

Open `http://localhost:8000` after `npm run dev`. In devtools console, run:

```javascript
console.log(typeof window.WsScrcpy.installThemeEmbedListener);  // 'function'
console.log(typeof window.WsScrcpy.getTheme);                   // 'function'
console.log(typeof window.WsScrcpy.setTheme);                   // 'function'
console.log(typeof window.WsScrcpy.notifyThemeReady);           // 'function'
```

Expected: All four print `'function'`.

- [ ] **Step 4: Commit**

```bash
git add src/app/public/index.ts
git commit -m "feat(theme): re-export themeEmbed API on public WsScrcpy surface"
```

---

## Task 7: Update README with theme bridge docs

**Repo:** `ws-scrcpy-web`

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Find the existing embedding section**

Run: `grep -n -i "embed\|iframe" README.md | head -10`

Identify the best place to add a new "Embedding: theme bridge" subsection.

- [ ] **Step 2: Add the section**

After the existing embedding/integration section, add:

````markdown
### Embedding: theme bridge

When ws-scrcpy-web is embedded in a cross-origin iframe, the host page can sync
its dark/light theme via postMessage.

**Protocol:**

| Direction | Message type | Payload | When |
|-----------|--------------|---------|------|
| iframe → parent | `ws-scrcpy-web:theme-ready` | `{theme: 'dark' \| 'light'}` | On load |
| parent → iframe | `ws-scrcpy-web:theme` | `{theme: 'dark' \| 'light'}` | In response to handshake; on every host theme change |

ws-scrcpy-web installs the listener and posts the handshake automatically. The
host page is responsible for the parent half:

```javascript
window.addEventListener('message', (e) => {
    if (e.data?.type === 'ws-scrcpy-web:theme-ready') {
        const iframe = document.getElementById('ws-scrcpy-iframe');
        iframe.contentWindow.postMessage(
            { type: 'ws-scrcpy-web:theme', theme: getMyHostTheme() },
            e.origin,
        );
    }
});
```

**Programmatic API:**

```javascript
WsScrcpy.getTheme();                       // 'dark' | 'light'
WsScrcpy.setTheme('light');                // applies + persists
WsScrcpy.installThemeEmbedListener();      // already called on load
WsScrcpy.notifyThemeReady();               // already called on load
```

**Security: `allowedOrigins`.** The default listener accepts theme messages
from any origin (`allowedOrigins: '*'`). This is permissive by design so the
helper is drop-in for any embedder. Locked-down deployments should call
`installThemeEmbedListener({ allowedOrigins: ['https://your-host.example'] })`
themselves and skip the auto-install (set a build flag, or fork
`src/app/index.ts`).
````

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs(theme): document embed theme bridge protocol and API"
```

---

## Task 8: Update CHANGELOG `[Unreleased]`

**Repo:** `ws-scrcpy-web`

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add entry to `[Unreleased]`**

Open `CHANGELOG.md`. Under the `[Unreleased]` heading (or create one if absent, just below the Keep a Changelog header), add:

```markdown
### Added

- Public theme bridge API (`themeEmbed`): `getTheme`, `setTheme`,
  `installThemeEmbedListener`, `notifyThemeReady`. Allows cross-origin host
  pages embedding ws-scrcpy-web in an iframe to sync dark/light theme via
  `postMessage`. Two-message protocol: iframe posts `ws-scrcpy-web:theme-ready`
  on load; host replies with `ws-scrcpy-web:theme`. Auto-installed on page
  load; standalone usage is a no-op.

### Changed

- `ThemeToggle` now imports `getTheme`/`setTheme` from `public/themeEmbed`
  (single source of truth). No behavior change.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs(changelog): theme bridge API under [Unreleased]"
```

---

## Task 9: Cut v0.1.24-beta.4 release (CI build, user tests)

**Repo:** `ws-scrcpy-web`

Per user direction (2026-04-29): I bump version + promote CHANGELOG + tag + push to trigger the GitHub Actions release workflow. User downloads the resulting artifact and tests it before we proceed to CM-side tasks.

- [ ] **Step 1: Bump version (all three locations)**

ws-scrcpy-web syncs versions across `package.json`, `Cargo.toml`, and the git tag — there's a CI-enforced check (`scripts/assert-version-sync.mjs`) that fails the release workflow if they drift. **Use the helper script, not manual edits:**

```bash
npm run version:bump 0.1.24-beta.4
```

This updates all three. Verify with `git diff` that `package.json`, `package-lock.json`, AND `Cargo.toml` all show the new version.

- [ ] **Step 2: Promote `[Unreleased]` to `[0.1.24-beta.4]`**

Edit `CHANGELOG.md`. Change the `[Unreleased]` heading to `[0.1.24-beta.4] - 2026-04-29` (use today's actual date). Add a fresh empty `[Unreleased]` block above it.

- [ ] **Step 3: Commit + tag + push**

```bash
git add package.json package-lock.json CHANGELOG.md
git commit -m "chore(release): v0.1.24-beta.4 — iframe theme bridge"
git tag v0.1.24-beta.4
git push origin feature/v0.1.24
git push origin v0.1.24-beta.4
```

- [ ] **Step 4: Verify CI release workflow kicks off**

Open the GitHub Actions tab for the repo — confirm the release workflow runs against the `v0.1.24-beta.4` tag. Wait for it to complete (typically the same window as prior beta builds).

- [ ] **Step 5: Notify user with the release URL**

Surface to the user:
> "v0.1.24-beta.4 tag pushed and CI release workflow triggered. Once the artifact publishes, download and install/test. Theme bridge changes:
> - `WsScrcpy.installThemeEmbedListener()` and `notifyThemeReady()` auto-installed
> - 4-message protocol: `theme-ready`, `theme-changed` (iframe → parent); `theme`, `theme-request` (parent → iframe)
> - Standalone behavior unchanged — bridge is no-op when not embedded.
> Confirm beta.4 looks good before I start CM-side tasks (Task 10+)."

Wait for user confirmation before starting Task 10.

---

## Task 10: Bump ws-scrcpy-web sidecar in Control Menu

**Repo:** `control-menu`

**Files:**
- Modify: location TBD at execution time. CM consumes ws-scrcpy-web as a sidecar — the version pin lives somewhere in the dependency manager / `WsScrcpyService.cs` / a `.props` or config file.

- [ ] **Step 1: Locate the ws-scrcpy-web version reference**

Run: `cd C:/Users/jscha/source/repos/control-menu && grep -rn "0.1\." --include="*.cs" --include="*.props" --include="*.json" --include="*.md" src/ControlMenu | head -20`

Identify the file that pins the bundled ws-scrcpy-web version.

- [ ] **Step 2: Bump to the v0.1.24 beta that includes the theme-bridge commits**

Edit the identified file: replace the old version pin with the user-confirmed beta tag (e.g., `0.1.24-beta.4`). If the integration is a vendored binary, also replace the binary in `dependencies/` with the matching release artifact.

- [ ] **Step 3: Build and confirm CM still starts**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj -c Release`
Expected: Build succeeds.

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release`
Expected: App starts at http://localhost:5159, AndroidPowerTools page loads the iframe pointing at v0.2.0.

- [ ] **Step 4: Commit**

```bash
git add <changed-files>
git commit -m "chore(deps): bump ws-scrcpy-web sidecar to v0.2.0"
```

---

## Task 11: Add `id` to the iframe element

**Repo:** `control-menu`

**Files:**
- Modify: `src/ControlMenu/Modules/AndroidPowerTools/Pages/AndroidPowerToolsPage.razor`

- [ ] **Step 1: Add `id` attribute**

Edit `src/ControlMenu/Modules/AndroidPowerTools/Pages/AndroidPowerToolsPage.razor`. Find:

```razor
        <iframe src="@WsScrcpy.BaseUrl/" class="power-tools-iframe"
                allow="autoplay; fullscreen; clipboard-read; clipboard-write"
                title="ws-scrcpy-web"></iframe>
```

Replace with:

```razor
        <iframe id="ws-scrcpy-iframe" src="@WsScrcpy.BaseUrl/" class="power-tools-iframe"
                allow="autoplay; fullscreen; clipboard-read; clipboard-write"
                title="ws-scrcpy-web"></iframe>
```

- [ ] **Step 2: Build and visually verify**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeds.

Open the AndroidPowerTools page in CM. View source, confirm `id="ws-scrcpy-iframe"` is present.

- [ ] **Step 3: Commit**

```bash
git add src/ControlMenu/Modules/AndroidPowerTools/Pages/AndroidPowerToolsPage.razor
git commit -m "feat(theme): add id to ws-scrcpy iframe for bridge selector"
```

---

## Task 12: Add `scrcpyThemeBridge.js`

**Repo:** `control-menu`

**Files:**
- Create: `src/ControlMenu/wwwroot/js/scrcpyThemeBridge.js`

- [ ] **Step 1: Create the bridge module**

Create `src/ControlMenu/wwwroot/js/scrcpyThemeBridge.js`:

```javascript
(function () {
    'use strict';

    var IFRAME_SELECTOR = '#ws-scrcpy-iframe';
    var READY_TYPE = 'ws-scrcpy-web:theme-ready';
    var CHANGED_TYPE = 'ws-scrcpy-web:theme-changed';
    var PUSH_TYPE = 'ws-scrcpy-web:theme';

    var settingFromIframe = false;

    function currentTheme() {
        return (window.themeManager && window.themeManager.get()) || 'dark';
    }

    function postToIframe(iframe, theme, targetOrigin) {
        if (!iframe || !iframe.contentWindow) return;
        try {
            iframe.contentWindow.postMessage({ type: PUSH_TYPE, theme: theme }, targetOrigin);
        } catch (e) {
            // Iframe not yet ready or cross-origin error; ignore.
        }
    }

    // Listen for iframe handshakes and changed-events from ws-scrcpy-web.
    window.addEventListener('message', function (event) {
        var data = event.data;
        if (!data || typeof data !== 'object') return;

        if (data.type === READY_TYPE) {
            // Iframe just loaded — reply with our current theme.
            if (event.source && typeof event.source.postMessage === 'function') {
                event.source.postMessage(
                    { type: PUSH_TYPE, theme: currentTheme() },
                    event.origin,
                );
            }
            return;
        }

        if (data.type === CHANGED_TYPE) {
            // ws-scrcpy-web's own UI changed the theme — sync CM to match.
            // Validate payload before accepting.
            if (data.theme !== 'dark' && data.theme !== 'light') return;
            if (data.theme === currentTheme()) return; // already in sync
            if (window.themeManager && typeof window.themeManager.set === 'function') {
                // Set a guard so notify() doesn't echo back to the iframe.
                settingFromIframe = true;
                try {
                    window.themeManager.set(data.theme);
                } finally {
                    settingFromIframe = false;
                }
            }
            return;
        }
    });

    // Public: called by themeManager.set/toggle to push a new theme to all
    // currently-mounted ws-scrcpy iframes. No-op when the theme change
    // originated from the iframe (avoids echo).
    window.scrcpyThemeBridge = {
        notify: function (theme) {
            if (settingFromIframe) return;
            var iframes = document.querySelectorAll(IFRAME_SELECTOR);
            for (var i = 0; i < iframes.length; i++) {
                var iframe = iframes[i];
                var origin;
                try {
                    origin = new URL(iframe.src, window.location.href).origin;
                } catch (e) {
                    origin = '*';
                }
                postToIframe(iframe, theme, origin);
            }
        },
        // Optionally request a re-announce from the iframe (covers the case
        // where this script attached its message listener after the iframe
        // already posted theme-ready). Currently unused — the bridge script
        // tag is registered above the iframe element, so the listener is
        // always attached first. Available for defensive callers.
        requestReady: function () {
            var iframes = document.querySelectorAll(IFRAME_SELECTOR);
            for (var i = 0; i < iframes.length; i++) {
                var iframe = iframes[i];
                if (!iframe.contentWindow) continue;
                var origin;
                try {
                    origin = new URL(iframe.src, window.location.href).origin;
                } catch (e) {
                    origin = '*';
                }
                try {
                    iframe.contentWindow.postMessage(
                        { type: 'ws-scrcpy-web:theme-request' },
                        origin,
                    );
                } catch (e) {
                    // ignore
                }
            }
        },
    };
})();
```

- [ ] **Step 2: Commit**

```bash
git add src/ControlMenu/wwwroot/js/scrcpyThemeBridge.js
git commit -m "feat(theme): add scrcpyThemeBridge interop for cross-origin iframe theme sync"
```

---

## Task 13: Hook `themeManager` and register the bridge script

**Repo:** `control-menu`

**Files:**
- Modify: `src/ControlMenu/wwwroot/js/theme.js`
- Modify: the file containing the existing `<script src="js/theme.js">` tag (likely `App.razor` or `_Host.cshtml`)

- [ ] **Step 1: Find the existing `theme.js` script tag**

Run: `cd C:/Users/jscha/source/repos/control-menu && grep -rn 'js/theme.js' src/ControlMenu --include="*.razor" --include="*.cshtml" --include="*.html"`

Note the file path that includes `<script src="...js/theme.js"></script>`.

- [ ] **Step 2: Register the bridge script**

In the file from Step 1, add a line immediately after the existing `theme.js` script tag:

```html
<script src="js/scrcpyThemeBridge.js"></script>
```

- [ ] **Step 3: Hook `themeManager.set` to notify the bridge**

Edit `src/ControlMenu/wwwroot/js/theme.js`. Replace the `set` function body — find:

```javascript
    set: function (theme) {
        localStorage.setItem(this._storageKey, theme);
        document.documentElement.setAttribute('data-theme', theme);
    },
```

Replace with:

```javascript
    set: function (theme) {
        localStorage.setItem(this._storageKey, theme);
        document.documentElement.setAttribute('data-theme', theme);
        if (window.scrcpyThemeBridge) {
            window.scrcpyThemeBridge.notify(theme);
        }
    },
```

(The bridge handles the no-iframe case internally — `querySelectorAll` returns an empty list when the AndroidPowerTools page isn't mounted.)

- [ ] **Step 4: Build and confirm**

Run: `dotnet build src/ControlMenu/ControlMenu.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/ControlMenu/wwwroot/js/theme.js <step-1-file>
git commit -m "feat(theme): wire themeManager.set to scrcpyThemeBridge.notify"
```

---

## Task 14: Manual integration test all four combos

**Repo:** `control-menu`

**Test plan:**

For each starting state, navigate to `/android-power-tools`, then perform the action, then verify both CM chrome and the embedded ws-scrcpy-web iframe agree on the theme.

| # | CM start | ws-scrcpy start | Action | Expected |
|---|----------|-----------------|--------|----------|
| 1 | dark | dark | Toggle CM to light | Both light |
| 2 | light | light | Toggle CM to dark | Both dark |
| 3 | dark | light | Reload page | Both dark (iframe handshake adopts CM) |
| 4 | light | dark | Reload page | Both light |
| 5 | dark | dark | Click ws-scrcpy-web's own toggle in iframe | Both light (iframe → parent sync via theme-changed) |
| 6 | light | light | Click ws-scrcpy-web's own toggle in iframe | Both dark |

Plus regression checks:
- Navigate away from `/android-power-tools`, toggle theme, navigate back. Iframe theme matches.
- Toggle theme rapidly 5 times. No console errors. Final state syncs.
- Open browser devtools, watch the Console + Network tab on the iframe. Confirm postMessage events fire (instrument with a temporary `console.log` if needed).

- [ ] **Step 1: Run all four scenarios**

Run: `dotnet run --project src/ControlMenu/ControlMenu.csproj -c Release`
Open: `http://localhost:5159/android-power-tools`

Step through the 4 scenarios + 3 regression checks. Document any failures.

- [ ] **Step 2: Update CHANGELOG and TECHNICAL_GUIDE**

Edit `CHANGELOG.md`. Under `[Unreleased]`:

```markdown
### Added

- Iframe theme bridge for the AndroidPowerTools page: ws-scrcpy-web's
  embedded theme now follows Control Menu's theme via cross-origin
  postMessage. Requires bundled ws-scrcpy-web v0.1.24-beta.4 or later.
```

Edit `docs/TECHNICAL_GUIDE.md`. Find the AndroidPowerTools section and add a paragraph:

```markdown
**Theme sync.** The embedded ws-scrcpy-web iframe receives theme updates from
Control Menu via postMessage. The bridge lives in
`wwwroot/js/scrcpyThemeBridge.js` and uses the public theme API ws-scrcpy-web
ships from v0.1.24-beta.4 onward. See
`docs/superpowers/specs/2026-04-29-iframe-theme-bridge-design.md` for the
protocol.
```

- [ ] **Step 3: Final commit**

```bash
git add CHANGELOG.md docs/TECHNICAL_GUIDE.md
git commit -m "docs: theme bridge changelog + technical guide entry"
```

---

## Done criteria

- [ ] ws-scrcpy-web theme-bridge commits landed on `feature/v0.1.24` and rolled into a beta release (≥ v0.1.24-beta.4) by the user.
- [ ] CM bundles the matching ws-scrcpy-web beta.
- [ ] All 4 manual integration scenarios pass.
- [ ] Both repos' CHANGELOGs updated.
- [ ] No regressions in standalone ws-scrcpy-web (theme toggle still works when not embedded).
- [ ] No console errors in CM or ws-scrcpy-web during integration testing.

---

## Post-implementation

- Update `todo_control_menu.md` — move "Auto-sync ws-scrcpy-web iframe theme with CM theme" from active to Shipped.
- Update `todo_ws_scrcpy_web.md` — remove "v0.2.0 minor — public theme embed helper" from active.
- Memory MEMORY.md index entries for both todos: bump last-updated date.
