# D2 - Runtime Dependency-Update Integrity (A+C Hybrid) - Design

- Status: Approved (design + spec review); per-dep tiers verified 2026-06-17 -> writing-plans
- Date: 2026-06-17
- Source: Finding #3 / decision D2 from the 2026-06-14 security/code-review audit
  (ledger: `reference_control_menu_security_review.md`)
- Scope: the **runtime in-app dependency updater** only. CM's own Velopack
  self-update is a separate concern (audit findings #14/#15).

## Problem (current behavior)

`DependencyManagerService.DownloadAndInstallAsync`
(`src/ControlMenu/Services/DependencyManagerService.cs:249`) updates a managed
binary by:

1. Download the asset over HTTPS to temp (`:273-298`). `HttpClient` auto-follows
   redirects, and on redirect **persists the new URL** as the dependency's stored
   `DownloadUrl` (`:278-283`).
2. Extract zip/tar.gz (`:300-314`).
3. "Verify" by **running the freshly downloaded executable** (`_executor.ExecuteAsync(newExe, verifyArgs)`, `:316-329`).
4. Swap into the install path (`:331-373`).

There is **no integrity or authenticity check before extract/run**. The first
time CM validates a download is by executing it. The entire trust model is "it
arrived over HTTPS from the configured URL." A compromised upstream release, a
hijacked direct URL, a TLS-defeating MITM, or a redirect/DNS attack therefore
leads CM to download and execute an attacker-controlled binary as the current
user. The redirect-persist behavior compounds this: one malicious redirect
permanently repoints future downloads for that dependency.

## Threat model

Loopback-only Blazor Server desktop app, no remote attack surface. The reachable
adversary is: a compromised upstream release artifact, a hijacked/MITM'd download
transport, or a redirect/DNS attack. The updater ships to end users, so the fix
must protect real installs, not just the dev box.

## Decision

**D2 = Option (c): A+C hybrid.** Preserve today's "update to upstream-latest" UX.
Verify each download with the strongest available tier; always enforce transport
hardening; when no cryptographic tier can verify (genuinely unverifiable
upstream), require explicit one-click user confirmation rather than blocking.

Cross-reference: **D1 (install-root ACL elevation-of-privilege) is decided
separately = leave the ACL as-is, matched to upstream. Final.** No code change;
not part of this spec.

## Scope

In scope - the 6 binaries CM downloads-and-executes at runtime:
`adb`, `sqlite3`, `magick`, `vtracer`, `go2rtc`, `potrace`.
Also in scope: adding `.7z` extraction to the runtime updater (magick's asset is
`.7z`, which `DownloadAndInstallAsync` cannot currently extract - see Components 5).

Out of scope:
- `ws-scrcpy-web` and `docker` - declared `Manual`/external, never downloaded by `DownloadAndInstallAsync`.
- The build-time `scripts/dependencies/fetch-*.ps1` seed fetchers - already SHA-pinned.
- CM's own Velopack self-update (findings #14/#15).

## Grounded per-dependency coverage (probed + signature-verified 2026-06-17)

| Dep | T1 pinned hash (we vet) | T2 upstream checksum | T3 Authenticode | Floor for a brand-new version |
|-----|-------------------------|----------------------|-----------------|-------------------------------|
| sqlite3 | yes | SHA3-256 on download page (strong) | unlikely | strong |
| magick  | yes | `.intoto.jsonl` attestation lists per-artifact SHA-256 (strong) | installers signed; portable `.7z` unconfirmed | strong |
| adb     | yes | skipped - only SHA-1 in `repository2-3.xml` (rejected as weak) | **Authenticode, Google LLC EV cert, sha256RSA - VERIFIED** | strong (T3) |
| go2rtc  | yes | NONE (raw binaries only) | none - **VERIFIED NotSigned** | transport-only |
| vtracer | yes | NONE (raw archives only) | none - **VERIFIED NotSigned** | transport-only |
| potrace | yes (pinned 1.16) | n/a | n/a | always T1 |

Key consequence: **the pinned-hash tier (T1) is the backbone.** Beyond it, sqlite
and magick have strong T2 checksums; **adb has a strong T3 - its binary is
Authenticode-signed by Google on an EV cert (sha256RSA), verified 2026-06-17, so
the legacy SHA-1 in the repo XML is ignored**; go2rtc and vtracer have neither
checksums nor signatures (verified unsigned). So only **go2rtc and vtracer**, and
only on a version newer than anything we have pinned, fall through to
transport-only - which is exactly the case the Tier-4 confirmation dialog exists
for.

## Architecture

A verification pipeline inserted into `DownloadAndInstallAsync` between
download-to-temp (`:298`) and extract (`:300`). Nothing unverified is extracted
or executed.

### Stage 0 - Transport hard gate (always, every download)

- Require `https` scheme.
- Redirects are permitted but the **final response host must be on the
  dependency's `AllowedHosts` allowlist**. (GitHub release assets 302-redirect
  from `github.com` to a `githubusercontent.com` CDN host - confirm the exact
  host at implementation and allowlist it. SourceForge downloads may redirect to
  a mirror host - see Open Observations.)
- Do **not** silently persist a redirected URL that lands off the allowlist
  (fixes `:278-283`).
- A transport failure is a hard fail (abort, clean temp, no extract/run).

### Stages 1-3 - Cryptographic verification (best available tier)

- **T1 Pinned hash:** if a vetted SHA-256 exists for `(dep, version)`, require an
  exact match.
- **T2 Upstream checksum:** else if the dep declares a `ChecksumSource`, fetch the
  upstream-published digest, compute the matching algorithm, compare.
- **T3 Authenticode:** else if the dep declares an `ExpectedSigner` and the binary
  is signed, verify the signature and publisher. (Concrete user: **adb**, pinned
  to `CN=Google LLC` on Google's EV code-signing cert.)
- **Any tier that runs and mismatches is a hard fail, always** (tampering
  detected). A tier that is unavailable (no pinned hash / checksum source
  unreachable / unsigned) falls through to the next tier.
- When T1 or T2 cryptographically confirms the bytes, the host allowlist is
  defense-in-depth (content integrity is already assured). When falling to
  Tier 4, the host allowlist is the primary remaining control and is strictly
  required.

### Stage 4 - Unverifiable: explicit user confirmation

Reached only when transport passed but **no** cryptographic tier could verify
(unknown version + no upstream checksum + unsigned). In practice this is only
go2rtc / vtracer on a brand-new version. The updater surfaces a one-click
confirmation dialog (content below). Accept -> proceed; decline -> abort with a
clear status and no state change.

### Insertion point

`DownloadAndInstallAsync`: new verification call after the temp file is written
(`:298`), before the extract block (`:300`). The current run-to-verify at
`:316-329` stays as a functional check **after** integrity is established, not as
the integrity gate.

## Components

1. **`ModuleDependency` integrity fields** (new, all optional except hosts):
   - `IReadOnlyDictionary<string,string> KnownHashes` - version -> SHA-256 (T1).
   - `ChecksumSource?` - `{ UrlOrTemplate, Format, Algorithm }` where
     `Format in { SqliteDownloadPage, InTotoJsonl, Sha256SumsFile }`
     and `Algorithm in { Sha256, Sha3_256 }` (T2).
   - `string? ExpectedSigner` - Authenticode subject (T3).
   - `string[] AllowedHosts` - permitted final download hosts (Stage 0).
2. **`IArtifactVerifier` / `ArtifactVerifier`** - new service:
   `Task<VerificationResult> VerifyAsync(string filePath, ModuleDependency dep, string version, CancellationToken ct)`
   returning `VerificationResult(bool Verified, VerificationTier Tier, string? Algorithm, string Detail)`,
   `VerificationTier in { PinnedHash, UpstreamChecksum, Authenticode, Unverified }`.
   Per-format checksum parsers are internal strategies.
3. **Transport policy** - configure the `dependency-updates` HttpClient so the
   final host is validated against `AllowedHosts` (custom handler or post-response
   check), and the redirect-persist path is gated.
4. **Tier-4 confirmation UI** - a dialog component shown when
   `Tier == Unverified`. See Data Flow for the round-trip.
5. **`.7z` extraction (SharpCompress, managed).** The updater's extract step
   (`:300-314`) handles only `.zip`/`.tar.gz`; magick's asset is `.7z`, so magick
   updates currently fail at `FindExecutable`. Add `.7z` support via the managed
   **SharpCompress** library (compiled into the app - satisfies
   Local-Dependencies-Only with no native binary). magick's `.7z` codec is
   `LZMA2 + BCJ` (verified 2026-06-17; the simple x86 filter, **not** the
   SharpCompress-unsupported BCJ2). The portable archive extracts flat
   (`magick.exe` + DLLs + `policy.xml` at the root). Validate extraction against
   this exact archive in an early unit test; if SharpCompress cannot handle the
   BCJ filter, fall back to a vendored `7za.exe` resolved via
   `IDependencyPathResolver` (never PATH). (A vendored 7za would also let finding
   #17 + the build's system-7-Zip reliance in `_Fetcher.ps1:91-100` collapse onto
   one binary - noted, not chosen here.)

## Data flow

download -> temp
  -> Stage 0 transport gate (fail -> abort)
  -> ArtifactVerifier.VerifyAsync
       -> Verified (T1/T2/T3)            -> continue to extract/run/swap
       -> hard mismatch                  -> abort (integrity failure)
       -> Unverified (transport-only)    -> UI confirmation
                                              accept -> continue
                                              decline -> abort

Because the confirmation needs a UI round-trip (Blazor Server), the unverified
case cannot block server code waiting on the user. Agreed v1 mechanic: a
`bool allowUnverified = false` parameter on `DownloadAndInstallAsync`. First call
(`false`): if it reaches Tier 4, return a result flagged
`NeedsUnverifiedConfirmation` carrying tool/version/host/detail; the page shows
the dialog; on accept it re-invokes with `allowUnverified = true`. (Re-download on
confirm is acceptable - the artifact is unverified either way and the pipeline
re-runs.) Exact temp-handling is for the implementation plan.

## Error handling

- Transport gate fail (non-HTTPS / off-allowlist host / off-allowlist redirect):
  `UpdateResult(false, null, "Blocked: download host not allowed (<host>)", ...)`; no extract/run.
- Cryptographic mismatch (T1/T2/T3): `UpdateResult(false, null, "Integrity check failed: <detail>", ...)`; no extract/run; temp cleaned.
- Checksum source unreachable: log, fall through to the next tier (a network blip
  fetching a checksum must not hard-fail; it degrades, ultimately to Tier 4).
- User declines Tier-4: `UpdateResult(false, null, "Update cancelled - could not be verified", ...)`; no state change.
- All abort paths clean the temp dir (existing `finally` at `:389-397`).

## Tier-4 confirmation dialog

Intent (required content):
- State plainly that this update **could not be cryptographically verified**.
- Make clear **the upstream maintainer of `<tool>` has chosen not to publish
  checksums or signatures** for their releases - so neither CM nor anyone can
  cryptographically confirm the download. This is an upstream choice, not a CM
  limitation.
- CM uses `<tool>` because it is well-designed; even so, the user should be
  **mindful** that accepting means trusting the update without independent
  verification, and is encouraged to verify the download themselves if they have
  any concern.
- Confirm that only HTTPS and the expected publisher host were verified.
- A single acceptance click proceeds; declining cancels.

Draft copy (final wording to be polished in UI):

> **"`<tool>` `<version>` could not be cryptographically verified"**
>
> The maintainer of `<tool>` does not publish checksums or signatures for their
> releases, so this update cannot be cryptographically confirmed - only that it
> arrived over HTTPS from the expected source (`<host>`).
>
> Control Menu uses `<tool>` because it is well-built, but you should be mindful:
> installing an update we cannot verify means trusting it as delivered. If you
> have any concern, verify the download yourself before accepting.
>
> `[ Cancel ]`  `[ Install anyway ]`

## Per-dependency configuration (initial; confirm exact hosts at implementation)

| Dep | AllowedHosts (final, incl. CDN/mirror) | ChecksumSource | ExpectedSigner (T3) |
|-----|----------------------------------------|----------------|---------------------|
| adb | dl.google.com | none (SHA-1 rejected as weak) | **CN=Google LLC** (EV; verified) |
| sqlite3 | sqlite.org | SqliteDownloadPage (SHA3-256) | none |
| magick | github.com, *.githubusercontent.com | InTotoJsonl (SHA-256) | if signed; confirm at impl |
| vtracer | github.com, *.githubusercontent.com | none | none (verified unsigned) -> Tier 4 |
| go2rtc | github.com, *.githubusercontent.com | none | none (verified unsigned) -> Tier 4 |
| potrace | potrace.sourceforge.net (+ SF mirror, see below) | none (pinned) | none |

## Testing strategy (TDD)

- `ArtifactVerifier` unit tests, per tier and per format, using **captured sample
  payloads** (no live network in tests):
  - T1 pinned: exact match passes; one flipped byte hard-fails; unknown version -> falls through.
  - T2 sqlite SHA3-256: parse page + match; mismatch hard-fails.
  - T2 magick in-toto: extract artifact SHA-256 + match.
  - T3 adb Authenticode: valid Google-signed passes; wrong/absent signer hard-fails; unsigned -> falls through.
  - Tier 4: unknown + no checksum + unsigned -> `Unverified` (go2rtc/vtracer shape).
- Transport: off-allowlist final host rejected; non-HTTPS rejected; off-allowlist redirect rejected.
- `.7z` extraction: SharpCompress extracts the exact magick `LZMA2+BCJ` archive to
  the expected flat layout (magick.exe + DLLs + policy.xml); a corrupt `.7z` fails
  cleanly. (This test is the go/no-go for SharpCompress vs. the vendored-7za fallback.)
- `DownloadAndInstallAsync` integration: tampered artifact never extracted;
  declined confirmation aborts cleanly with no state change; verified path installs.

## Maintenance lifecycle

T1 coverage must stay current so the weak Tier-4 floor is rarely hit. Provide a
maintainer mechanism - e.g. `scripts/update-dependency-hashes.ps1` (ASCII-only),
optionally run on a schedule in CI - that resolves each dep's current
upstream-latest, downloads it, computes SHA-256, updates `KnownHashes`, and opens
a PR. Future consistency option (not in this spec): have the build-time
`fetch-*.ps1` read the same pinned hashes so there is a single source of truth.

## Open observations / assumptions

1. **magick `.7z` runtime extraction - now in scope.** `DownloadAndInstallAsync`
   extracts only `.zip`/`.tar.gz` (`:300-314`); magick's asset is `.7z`, so
   magick's runtime auto-update is inert today (fails at `FindExecutable`).
   Resolved in this design by adding managed SharpCompress `.7z` support
   (Components 5); codec verified `LZMA2+BCJ`. The build-time `.7z` PATH reliance
   (finding #17) remains separate.
2. **GitHub asset redirects.** Release-asset downloads redirect `github.com` ->
   `*.githubusercontent.com`. The transport gate must allow redirects to an
   allowlisted CDN host, not forbid cross-host redirects outright. Confirm the
   exact CDN host(s) at implementation.
3. **SourceForge mirror redirects (potrace).** SourceForge downloads can redirect
   to a rotating mirror host. Because potrace is pinned (T1 hash authoritative),
   a mirror redirect is acceptable for it - host-pin is strict only on the
   unverified Tier-4 path. Confirm potrace's actual redirect behavior at
   implementation.
4. **adb resolved.** Verified 2026-06-17: adb's only repo-XML checksum is SHA-1
   (legacy, ignored), but `adb.exe` is Authenticode-signed by Google on an EV cert
   (sha256RSA, Status Valid). adb therefore uses T3, not the weak SHA-1 and not
   Tier-4. go2rtc and vtracer were verified `NotSigned`.

## Out of scope / future

- CM's own Velopack update signing (#14/#15).
- The CI/build `.7z` extractor PATH hardening (#17) - related but separate
  (runtime `.7z` extraction is now in scope via Components 5).
- Full SLSA provenance verification of the magick in-toto attestation (v1 only
  extracts the artifact SHA-256 from it; full chain verification is future work).
