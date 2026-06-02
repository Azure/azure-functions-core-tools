# Spec: `func update` (in-place CLI update)

## Goal

Add a `func update` command that updates the installed `func` CLI in place,
mirroring the relevant parts of `aspire update --self`. No `--self` flag, since
the func CLI has no separate project-pinning concern: `func update` always means
"update the CLI itself".

## User experience

```text
func update                       # update to latest stable release
func update --prerelease          # update to latest release including prereleases
func update --version 5.1.0       # pin to a specific version
func update -y / --yes            # skip confirmation prompts (required when non-interactive)
```

`--prerelease` matches the existing install scripts (`install.sh PRERELEASE=true`
/ `install.ps1 -Prerelease`) so the same mental model carries over.

Output is concise, themed via `IInteractionService` / `ITheme`:

1. Resolve channel (stable, or prerelease when `--prerelease`).
2. Show "Checking for updates…" spinner; print current + latest version.
3. If up to date, exit 0 with a one-liner.
4. If newer, prompt `Update func from X to Y? (Y/n)` (suppressed by `-y` / `--yes`).
5. Spinner: download archive (with progress if practical), extract, swap, verify.
6. Print new version on success, restore backup on any failure.

Exit codes: `0` success / already current, non-zero on failure
(`GracefulException` with a user-facing message for known failure modes).

## Channel resolution

Two release channels, matching the existing install-script behaviour:

| Channel | Source | Selector |
| --- | --- | --- |
| stable | latest non-prerelease GitHub release | default |
| prerelease | latest GitHub release including prereleases | `--prerelease` |

Precedence:

1. `--version X.Y.Z` (explicit, wins; `--prerelease` is still allowed for clarity).
2. `--prerelease` flag → prerelease channel.
3. Default → stable.

No per-project channel config.

### Version classification (SemVer)

Release classification uses [SemVer 2.0](https://semver.org/) on the GitHub
release tag, NOT the GitHub `prerelease` flag on the release object. The repo
already publishes tags in SemVer form:

| Tag           | SemVer parse                               | Channel    |
| ------------- | ------------------------------------------ | ---------- |
| `v5.0.0`      | `5.0.0` (no prerelease label)              | stable     |
| `v5.1.2`      | `5.1.2` (no prerelease label)              | stable     |
| `v5.0.0-preview.1` | `5.0.0-preview.1` (prerelease label)  | prerelease |
| `v5.0.0-rc.1` | `5.0.0-rc.1` (prerelease label)            | prerelease |

Rules:

- A release is **stable** iff `SemVersion.Prerelease` is empty.
- A release is **prerelease** iff `SemVersion.Prerelease` is non-empty
  (any label: `preview.N`, `rc.N`, `beta.N`, etc.).
- Tag → version: strip a leading `v`, then `SemVersion.Parse(value, SemVersionStyles.Strict)`.
  Tags that don't parse strictly are skipped with a debug log.
- "Latest" within a channel = the max `SemVersion` by SemVer precedence (NOT
  GitHub's `published_at` ordering), filtered to the channel set defined above.
- The GitHub release object's `prerelease` boolean is used only as a tiebreaker
  when SemVer alone is ambiguous (which it shouldn't be for our tags). The
  authoritative signal is the SemVer prerelease label.
- The running CLI's own version (from `AssemblyCliVersionProvider`) is parsed
  the same way, so "newer than current" comparisons use SemVer precedence.

This keeps `func update` and the install scripts in lockstep: both already
decide "is this a prerelease?" from the SemVer label, so a tag like
`v5.0.0-preview.1` is treated as prerelease everywhere without any extra
metadata.

We pick up [Semver](https://www.nuget.org/packages/Semver) (the library Aspire
uses) rather than `NuGet.Versioning`. It's a single small dependency that
implements SemVer 2.0 cleanly and matches the upstream reference we're
mirroring.

### Release channels vs deployment channels (future)

The `--prerelease` flag selects a **release channel** (which GitHub release to
fetch). It is intentionally distinct from the **deployment channel**, i.e. the
install method the user originally used (GitHub install script, npm, Homebrew,
Chocolatey, winget, MSI).

When we later support installing via npm / Homebrew / etc., `func update` MUST
keep the user on their original deployment channel:

- A user who installed via `npm i -g` should be told to run `npm i -g …` and
  never have their npm-owned files overwritten by an in-process binary swap.
- A Homebrew user should be told to run `brew upgrade`, and so on.
- Only the install-script deployment channel (`~/.azure-functions/func`) ever
  performs an in-process update.

The install-method detector (see below) is the mechanism that enforces this.
The `--prerelease` selector on `func update` still applies inside whichever
deployment channel the user is on (e.g. `npm i -g @azure/functions-core-tools@next`
for prerelease on npm), so the user-facing flag stays consistent across all
install methods.

## Install-method detection (must do, copy from Aspire)

`func update` MUST detect package-manager installs and **defer** to them instead
of overwriting their files:

| Detected install | Behaviour |
| --- | --- |
| npm (`@azure/functions-core-tools` global) | Print `npm i -g @azure/functions-core-tools@<tag>` and exit 0 |
| Homebrew (`brew list azure-functions-core-tools`) | Print `brew upgrade azure-functions-core-tools` and exit 0 |
| Chocolatey (`choco`) | Print `choco upgrade azure-functions-core-tools` and exit 0 |
| MSI / winget | Print `winget upgrade …` and exit 0 |
| Install script (`~/.azure-functions/func`) | Perform in-place update (this spec) |

Detection signals:

- npm: env vars set by the npm launcher (Aspire pattern) and/or process path under `node_modules`.
- brew: process path under `Cellar/`/`opt/`.
- choco: process path under `chocolatey/lib/`.
- winget: WindowsApps path or registry marker.
- Fallback: in-place update if `Environment.ProcessPath` is under the install-script target dir (`~/.azure-functions` by default, configurable via env).

If the binary is in an unrecognised location and not under `~/.azure-functions`,
fail with a `GracefulException` pointing the user to re-run the install script.

## In-place update flow (mirrors `ExtractAndUpdateAsync` in Aspire)

1. Resolve `currentExePath = Environment.ProcessPath`; derive `installDir`.
2. Resolve target asset from GitHub releases:

Endpoint: `https://api.github.com/repos/Azure/azure-functions-core-tools/releases` (filter by `prerelease` flag).Match asset by RID (`win-x64`, `osx-arm64`, `linux-x64`, etc.), same logic the install scripts already use; share that table.

3. Download the archive (`.zip` / `.tar.gz`) to a temp dir; validate SHA-256 against the release asset's published hash (the install scripts already do this; reuse).
4. Extract to a temp dir.
5. Locate the new `func` / `func.exe` in the extracted payload.
6. Rename current binary to `func.old.<unixTimestamp>` (backup).
7. Copy new binary into `installDir`; on Unix set executable bit.
8. Probe `func --version` on the new binary; if it fails or returns garbage, roll back from backup.
9. On success, sweep old `.old.*` siblings (Aspire's `FileDeleteHelper.TryCleanupOldItems`).
10. If `installDir` is not on `PATH`, print the same "add to PATH" hint the install script prints.

Important deltas from Aspire:

- None substantive. `func` ships as a single self-contained binary
  (`func` / `func.exe`), so the swap is the same single-file rename Aspire
  performs: extract → backup current as `.old.<timestamp>` → copy new in place
  → `chmod +x` on Unix → verify `func --version` → sweep old backups
  (rollback on failure).
- Sibling state (e.g. `~/.azure-functions/workloads`) MUST NOT be touched;
  workloads are managed by `func workload install/update` and survive a CLI
  update. The swap only ever writes the `func` binary itself.

## Cancellation / errors

- All async paths take a `CancellationToken`; Ctrl+C cancels cleanly.
- HTTP, archive, and filesystem errors are caught at the command boundary and
  wrapped in `GracefulException(message, isUserError: true)` with actionable
  text (e.g. "Run with sudo" on `UnauthorizedAccessException`, "Re-run the
  install script" if `installDir` is not writable).
- Unrecognised exceptions surface as runtime bugs (stack trace), per repo
  conventions.

## Code layout (proposed)

```text
src/Func/Commands/Update/
  UpdateCommand.cs          # FuncCliCommand + IBuiltInCommand, thin handler
  UpdateCommandOptions.cs   # parsed options DTO
  UpdateRunner.cs           # orchestrates check → download → swap → verify

src/Func/Update/
  IReleaseFeed.cs           # GitHub releases abstraction
  GitHubReleaseFeed.cs      # IHttpClientFactory-backed impl
  IInstallMethodDetector.cs # detects npm/brew/choco/winget/install-script
  InstallMethodDetector.cs
  ICliInstaller.cs          # extract + swap + verify + rollback
  CliInstaller.cs
  RidResolver.cs            # OS + arch → asset suffix (shared with install scripts spec)
```

All types `internal`, primary-constructor DI, no static helpers except pure
utilities. Wire registrations in `Program.cs` / DI module. Add to
`BuiltInCommands.cs` and `Parser.cs` per the `add-command` skill.

## Tests (xUnit + NSubstitute + AwesomeAssertions)

- `UpdateCommandTests` — option parsing, channel selection precedence, confirm prompt, `--yes` enforcement under non-interactive.
- `InstallMethodDetectorTests` — npm/brew/choco/winget/script paths, env-var overrides, unknown location.
- `CliInstallerTests` — fake filesystem + fake archive: happy path, version-probe failure → rollback, write-permission failure → rollback, partial extract failure → rollback, backup cleanup.
- `GitHubReleaseFeedTests` — `HttpMessageHandler` stub returning fixture payloads; stable vs prerelease filtering, asset matching by RID, hash field plumbing.

## Update notifier (in scope)

Modeled on Aspire's `ICliUpdateNotifier`: a low-cost, non-blocking check that
prints a single hint when a newer release is available, leaving the actual
update as the user's explicit `func update` invocation.

Behaviour:

- On every `func` invocation, a background task asks `IReleaseFeed` for the
  latest version on the current channel (stable by default; prerelease when
  the running build is a prerelease, mirroring the install-script auto-detect).
- The check is cached on disk under `~/.azure-functions/.update-check.json`
  (`{ checkedAt, latestVersion, channel }`). TTL: 24 hours. Cache hit → no
  network call.
- The check never blocks the command: it runs in parallel with the invoked
  command and is awaited briefly at shutdown. If it hasn't completed (or
  errored, or is rate-limited) the CLI exits without printing anything.
- When a newer version is detected, a single themed line is printed to stderr
  after the command's normal output:

  ```text
  A new func release is available: 5.1.0 → 5.2.0
  Run 'func update' to upgrade.
  ```

- Suppressed when:
  - `--quiet` / non-TTY stderr.
  - `FUNC_CLI_UPDATE_NOTIFIER=0` is set.
  - The current invocation IS `func update` (avoid double-printing).
  - The detected install method is a package manager (npm/brew/choco/winget):
    the hint instead points at the appropriate `upgrade` command, same as
    `func update` itself.
- Failures (network, JSON parse, FS) are logged at `Debug` and swallowed.
  The notifier MUST NOT change exit codes or surface stack traces.

Out of the notifier's scope (separate follow-up):
- Telemetry on hint-shown / hint-acted-on rates.
- Per-channel opt-out (the env var is global on/off for v1).

### Notifier code layout

```text
src/Func/Update/
  ICliUpdateNotifier.cs
  CliUpdateNotifier.cs        # background check + cache + render
  UpdateCheckCache.cs         # JSON read/write on ~/.azure-functions/.update-check.json
```

Wired in `Program.cs` so the notifier is started before command execution and
awaited (with a short timeout) during shutdown.

### Notifier tests

- `CliUpdateNotifierTests` — fresh check / cache hit / cache stale, env-var
  off, package-manager install path (no hint), `func update` invocation
  (suppressed), network failure (silent).
- `UpdateCheckCacheTests` — JSON round-trip, corrupted-file recovery, TTL
  boundary, concurrent-write race (best-effort, single writer wins).

## Out of scope (for this spec)

- Silent in-process auto-update on launch (download + swap without user action). Notifier only; the actual update remains an explicit `func update`.
- Updating across major versions with breaking workload changes — same UX, but the install method detector must refuse `--version <older-major>` downgrades unless `--force` is passed.
- Telemetry events (add once the telemetry surface stabilises).

## Open questions

1. Should `--version` accept a `5.x` floating spec? Aspire doesn't. Recommend: no, exact version only.
2. Hash validation: the existing install scripts already compute and verify SHA-256 from the release notes. Confirm where the canonical hash list lives so the in-process updater reads the same source.