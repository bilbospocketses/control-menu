# Iframe Theme Bridge — Design

**Date:** 2026-04-29
**Status:** Spec locked, awaiting implementation plan
**Repos affected:** `ws-scrcpy-web` (new public API, minor bump 0.1.x → 0.2.0) and `control-menu` (paired iframe consumer)

## Problem

When ws-scrcpy-web is embedded in Control Menu's iframe (`Modules/AndroidPowerTools/Pages/AndroidPowerToolsPage.razor:17`), its theme does not track Control Menu's theme. CM and ws-scrcpy-web run on different ports (5159 vs 8000) → cross-origin iframe → CM cannot write ws-scrcpy-web's `localStorage` or mutate its DOM directly. The minimum-required ws-scrcpy-web change is a `postMessage` listener; the corresponding CM change is an interop module that posts on toggle and on iframe load.

Goal: when the user toggles the theme in CM, the embedded ws-scrcpy-web view updates immediately. When the iframe (re)loads, it adopts CM's current theme automatically.

## Locked Decisions (from 2026-04-29 brainstorm)

1. **Message type string:** `ws-scrcpy-web:theme` (parent → iframe push) and `ws-scrcpy-web:theme-ready` (iframe → parent handshake). Namespace prefix matches the existing `ws-scrcpy-web-theme` localStorage key.
2. **Default `allowedOrigins`:** `'*'` with prominent doc warning. CM passes `[location.origin]` to lock it down. Permissive default chosen because the helper is intended to be drop-in for any embedder.
3. **Handshake direction:** iframe → parent posts `theme-ready` on load; parent replies with current theme. ("Handshake is civility, no bull in china shop.")
4. **Versioning:** ws-scrcpy-web minor bump `0.1.x → 0.2.0` — additive public-API surface, no breaking changes.
5. **Scope:** expose programmatic `getTheme` + `setTheme` alongside the listener helpers (option B from brainstorm — a fully usable embed API, not just a listener).

## Architecture

### ws-scrcpy-web side

**New file:** `src/app/public/themeEmbed.ts`

Public surface:

```ts
export type Theme = 'dark' | 'light';

export interface ThemeEmbedOptions {
  messageType?: string;            // default 'ws-scrcpy-web:theme'
  allowedOrigins?: '*' | string[]; // default '*' (with doc warning)
}

export function getTheme(): Theme;
export function setTheme(theme: Theme): void;
export function installThemeEmbedListener(opts?: ThemeEmbedOptions): () => void;
export function notifyThemeReady(target?: Window, opts?: ThemeEmbedOptions): void;
```

Behavior:

- **`getTheme` / `setTheme`** — promote the file-local helpers from `src/app/client/ThemeToggle.ts` (lines 6–13) to public exports. `setTheme` writes the `data-theme` DOM attribute and the `ws-scrcpy-web-theme` localStorage key, identical to today's behavior.
- **`installThemeEmbedListener(opts?)`** — `window.addEventListener('message', handler)`. The handler:
  - Validates `event.data?.type === opts.messageType` (default `'ws-scrcpy-web:theme'`).
  - Validates `event.data.theme ∈ {'dark', 'light'}`.
  - Validates `event.origin` is in `opts.allowedOrigins` if it's a list (skip check if `'*'`).
  - Calls `setTheme(event.data.theme)`.
  - Returns a disposer that calls `removeEventListener`.
- **`notifyThemeReady(target?, opts?)`** — fire-and-forget `target.postMessage({type: 'ws-scrcpy-web:theme-ready', theme: getTheme()}, '*')` to `target ?? window.parent`. Does nothing if `target` is null or equal to `window` (i.e., not embedded).

**Wire-up:** `src/app/index.ts` after `initTheme()` (line 138):

```ts
installThemeEmbedListener();
notifyThemeReady();
```

Three added lines (call + call + import).

**Re-exports:** `src/app/public/index.ts` adds:

```ts
export {
  getTheme,
  setTheme,
  installThemeEmbedListener,
  notifyThemeReady,
} from './themeEmbed';
export type { Theme, ThemeEmbedOptions } from './themeEmbed';
```

These land on `window.WsScrcpy.*` (UMD), as named ESM exports, and in the generated `.d.ts`.

**Refactor:** `src/app/client/ThemeToggle.ts` — replace its file-local `getTheme`/`setTheme` with imports from `../public/themeEmbed`. Single source of truth; no behavior change.

### Control Menu side

**New file:** `src/ControlMenu/wwwroot/js/scrcpyThemeBridge.js`

Responsibilities:
- Hook the AndroidPowerTools iframe element by id (or via a `@ref`-derived element ref passed in from Blazor).
- Listen for `message` events with `event.data?.type === 'ws-scrcpy-web:theme-ready'`. On match, post `{type: 'ws-scrcpy-web:theme', theme: window.themeManager.get()}` back to `event.source`, with `targetOrigin` set to `event.origin` (locks down to the ws-scrcpy-web origin without hardcoding it).
- Expose `window.scrcpyThemeBridge.notify(theme)` so CM's theme toggle can push updates after `themeManager.set(...)`.
- The `notify` function loops through all iframes matching the AndroidPowerTools selector and posts the new theme to each, using the iframe's resolved origin as `targetOrigin`.

**Wire-up:**
- `App.razor` (or `_Host.cshtml`) — add `<script src="js/scrcpyThemeBridge.js"></script>` after `theme.js`.
- The CM theme toggle (`window.themeManager.toggle`) gets a one-line addition: `window.scrcpyThemeBridge?.notify(next);` after the `set(next)` call. Edited inline in `wwwroot/js/theme.js`.
- `Modules/AndroidPowerTools/Pages/AndroidPowerToolsPage.razor:17` — add `id="ws-scrcpy-iframe"` to the iframe so the bridge can find it. No Blazor `@ref` needed; the JS module manages itself.

## Protocol Summary

| Direction | Message type | Payload | Notes |
|-----------|--------------|---------|-------|
| iframe → parent | `ws-scrcpy-web:theme-ready` | `{theme: 'dark'\|'light'}` | Sent on load by `notifyThemeReady()`; also re-sent in response to a `theme-request` ping |
| iframe → parent | `ws-scrcpy-web:theme-changed` | `{theme: 'dark'\|'light'}` | Sent when ws-scrcpy-web's own UI changes the theme (its in-app toggle button) — keeps the host in sync regardless of which side initiated the change |
| parent → iframe | `ws-scrcpy-web:theme` | `{theme: 'dark'\|'light'}` | Sent by parent in response to `theme-ready` and on every host theme toggle |
| parent → iframe | `ws-scrcpy-web:theme-request` | `{}` | Optional. Hosts that attach their `message` listener AFTER iframe load can post this to ask the iframe to re-announce `theme-ready`, eliminating the load-race footgun |

All messages are namespaced with the `ws-scrcpy-web:` prefix to avoid collisions with other postMessage traffic in the page.

### Why these four messages

The 2026-04-29 brainstorm locked in the first three (`theme-ready`, `theme`, plus the auto-install of the listener and `notifyThemeReady`). Two additions surfaced during code review of Task 5 (2026-04-29 implementation pass):

- **`theme-request` (parent → iframe).** Without it, a host that attaches its `message` listener inside `iframe.onload` may miss the iframe's one-shot `theme-ready` post (the iframe ships its handshake before `onload` fires). `theme-request` lets such a host pull a fresh `theme-ready` after they're listening — no silent failure mode for downstream embedders.
- **`theme-changed` (iframe → parent).** ws-scrcpy-web's own theme toggle button is rendered inside the iframe and is functional even when embedded. Without `theme-changed`, clicking it would diverge ws-scrcpy-web's theme from the host's. Posting `theme-changed` keeps both sides in sync regardless of which UI surface initiated the change.

## Tests

**ws-scrcpy-web:** `src/app/public/__tests__/themeEmbed.test.ts` (new)

Cases:
- `getTheme` returns `'dark'` when localStorage is empty (default fallback).
- `setTheme('light')` writes both DOM attribute and localStorage; `getTheme` reflects it.
- `installThemeEmbedListener` applies a valid theme message (`{type:'ws-scrcpy-web:theme', theme:'light'}`).
- Ignores wrong message type.
- Ignores invalid theme value (e.g., `'midnight'`, `null`).
- Ignores origin not in `allowedOrigins` list (when not `'*'`).
- Disposer detaches the listener — subsequent valid messages have no effect.
- `notifyThemeReady` posts `{type:'ws-scrcpy-web:theme-ready', theme: getTheme()}` to `window.parent` (mocked).
- `notifyThemeReady` is a no-op when `target === window` (no parent / standalone).

**Control Menu:** No automated tests for the JS bridge (existing CM JS modules have no test harness). Manual test all four combos: CM dark/light × ws-scrcpy-web dark/light; refresh and theme-toggle round-trip; verify iframe theme persists across navigations away from and back to the AndroidPowerTools page.

## Release Sequence (cross-repo ordering)

This is the load-bearing part of the design — get the order wrong and CM ships pointing at a non-existent API.

1. **ws-scrcpy-web first.** New file, listener wiring, public re-exports, refactor ThemeToggle, tests pass, docs (README "Embedding" section gets a theme-bridge subsection), CHANGELOG entry under `[Unreleased]`.
2. **Cut v0.2.0** — bump `package.json`, move `[Unreleased]` to `[0.2.0] - 2026-04-XX`, tag, push tag, CI publishes. Verify the `WsScrcpy.installThemeEmbedListener` symbol exists in the published UMD bundle.
3. **Control Menu second.** Bump the bundled ws-scrcpy-web dependency to v0.2.0 (CM ships ws-scrcpy-web as a sidecar — see `project_control_menu_wsscrcpy_integration.md`). Add `scrcpyThemeBridge.js`, edit `theme.js`, add `id` to iframe, register script tag.
4. **Manual test all four combos.** Ship CM commit.

If the order is reversed (CM first, ws-scrcpy-web second), CM's bridge sends `ws-scrcpy-web:theme` messages into a void — no listener attached — and the user sees "theme toggle does nothing for the iframe." Silent failure mode, easy to miss in dev.

## Documentation

**ws-scrcpy-web README** — new "Embedding: theme bridge" subsection covering:
- The two-message protocol with a sequence diagram (mermaid, text-only).
- Code example: parent-side handler that responds to `theme-ready`.
- The `allowedOrigins: '*'` default warning. Strong recommendation to pass `[location.origin]` or an explicit allowlist when the embedder is known.
- Programmatic API: when to call `getTheme`/`setTheme` vs. relying on the listener.

**Control Menu** — `docs/TECHNICAL_GUIDE.md` gets a one-paragraph mention under the AndroidPowerTools section explaining the bridge.

## Out of Scope

- **Theme change events from iframe → parent.** ws-scrcpy-web doesn't currently expose a UI to toggle its own theme when embedded; CM is the source of truth. If that changes, add a `'theme-changed'` message later — it's an additive minor bump on top of 0.2.0.
- **System theme tracking (`prefers-color-scheme`).** Both apps already ignore system preference. Out of scope for this work.
- **Color-scheme CSS variable parity.** ws-scrcpy-web has its own theme palette; we're syncing the *mode* (dark/light), not the palette. CM's theme tokens stay distinct.

## Open Items

- **ws-scrcpy-web branch base** — to be decided when implementation starts. Options: branch off `main` at `8522031` (v0.1.23 stable), branch off `feature/v0.1.24`, or wait for v0.1.24 to merge. Locked design said "off main post-Velopack"; current default leans toward `main` at `8522031`.

## Related Memories

- `reference_wsscrcpy_theme_vars.md` — ws-scrcpy-web's theme CSS variables and localStorage key
- `todo_control_menu.md` §"Auto-sync ws-scrcpy-web iframe theme" — locked decisions log
- `todo_ws_scrcpy_web.md` — paired entry under "v0.2.0 minor — public theme embed helper"
- `project_control_menu_wsscrcpy_integration.md` — CM ships ws-scrcpy-web as a sidecar; informs how CM consumes the bumped version
