# Security Policy

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Report security issues privately through GitHub's built-in security advisory flow:

**[Report a vulnerability](https://github.com/bilbospocketses/control-menu/security/advisories/new)**

This opens a private channel between you and the maintainer — no public disclosure until a fix is ready.

## What to Include

When reporting, please provide:

- A clear description of the vulnerability and its impact
- Steps to reproduce (proof-of-concept code, configuration, or network conditions)
- The affected version / commit
- Any mitigations you're aware of

## Response Expectations

- **Acknowledgement:** within **72 hours** of receipt
- **Triage and initial assessment:** within one week
- **Fix and disclosure timeline:** discussed with the reporter on a per-issue basis, depending on severity and complexity

## Supported Versions

Security fixes target the latest commit on `master`. Older commits are not maintained.

## Scope

In scope: the ASP.NET Core / Blazor Server app, its SQLite store, ADB / go2rtc / ws-scrcpy-web orchestration, the Imaging Tools' invocation of the bundled image binaries (ImageMagick, vtracer, potrace), email notification delivery, and the first-run wizard flow.

> The bundled ImageMagick is shipped with a hardened `policy.xml` (deny-by-default coders + a small format allowlist, known-CVE-historical coders denied explicitly, and resource caps) staged next to `magick.exe`. If you find a way to bypass that policy or to make the Imaging Tools process attacker-controlled input unsafely, that is in scope — please report it.

Out of scope:
- Vulnerabilities in upstream dependencies (ws-scrcpy-web, go2rtc, node-pty, EF Core, etc.) that have not been released against Control Menu — report those upstream.
- Issues requiring physical or console access to a host already running the app.
- Self-XSS or similar issues requiring the victim to paste attacker-controlled code into devtools.
- Anything that requires the reporter to have valid admin credentials on the host machine.

Thanks for helping keep the project safe.
