# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project will adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) once a numbered release is cut.

## [Unreleased]

### Changed
- `ScrcpyMirror.razor` now uses ws-scrcpy-web's new `/embed.html?device=<udid>` wrapper URL instead of the legacy `#!action=stream&udid=...&embed=true` hash routing. The legacy URL was removed in the ws-scrcpy-web 1.0.0 stream API rewrite; `/embed.html` is the stable public entry point. Both `Inline=true` (inline iframe) and `Inline=false` (popup window) now share the same URL — embed.html is always in embed mode (transparent background, mirror + toolbar only), so there's no longer a toggle.
- `docs/TECHNICAL_GUIDE.md` Stream URL section updated with the new URL pattern and a pointer to the ws-scrcpy-web TECHNICAL_GUIDE for optional URL parameters (codec, encoder, bitrate, etc.).
- `README.md` License section updated from "Private project" to GPL-3.0-only with links to LICENSE and CONTRIBUTING; added a Security section pointing at SECURITY.md.

### Added
- `CHANGELOG.md` (this file) following Keep a Changelog format.
- `LICENSE` — GPL-3.0-only (matches ws-scrcpy-web, the primary external dependency).
- `SECURITY.md` — private vulnerability reporting flow via GitHub Security Advisories, 72-hour acknowledgement SLA.
- `CONTRIBUTING.md` — setup, build/test commands, project structure, module architecture, code style, PR guidelines.
- `.gitignore` entries for `.claude/` (Claude Code session artifacts), `src/ControlMenu/dependencies/go2rtc/*` (runtime-managed binaries, matching existing pattern for other dep folders), and `src/ControlMenu/go2rtc.yaml` (runtime-generated config rebuilt on startup from the cameras table).
