# Spec: `func update` (in-place CLI update)

## Goal

Add a `func update` command that updates the installed `func` CLI in place,
mirroring the relevant parts of `aspire update --self`. No `--self` flag, since
the func CLI has no separate project-pinning concern: `func update` always means
"update the CLI itself".

## User experience

```text
func update                       # update to latest on the current channel (default: stable)
func update --channel preview     # update to latest on the preview channel
func update --version 5.1.0       # pin to a specific version
func update -y / --yes            # skip confirmation prompts (required when non-interactive)
```

Channel values: `stable` (default), `preview`. Case-insensitive. Unknown values
fail with a `GracefulException` listing the valid set.

Output is concise, themed via `IInteractionService` / `ITheme`:

1. Resolve channel (stable/preview).
2. Show "Checking for updates…" spinner; print current + latest version.
3. If up to date, exit 0 with a one-liner.
4. If newer, prompt `Update func from X to Y? (Y/n)` (suppressed by `-y` / `--yes`).
5. Spinner: download archive (with progress if practical), extract, swap, verify.
6. Print new version on success, restore backup on any failure.

Exit codes: `0` success / already current, non-zero on failure
(`GracefulException` with a user-facing message for known failure modes).

## Channel resolution

Two channels, matching the existing install-script behaviour:

| Channel | Source | Selector |
| --- | --- | --- |
| stable | latest non-prerelease GitHub release | default |
| preview | latest GitHub release including prereleases | `--channel preview` |

Precedence:

1. `--version X.Y.Z` (explicit, wins; `--channel` still resolves the feed).
2. `--channel <name>`.
3. Default → `stable`.

Channel values are case-insensitive. Unknown values fail with a
`GracefulException` listing the valid set. No per-project channel config.

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

## Out of scope (for this spec)

- Background "update available" notifier on every command (Aspire's `ICliUpdateNotifier`). Track separately.
- Auto-update on launch. Always explicit.
- Updating across major versions with breaking workload changes — same UX, but the install method detector must refuse `--version <older-major>` downgrades unless `--force` is passed.
- Telemetry events (add once the telemetry surface stabilises).

## Open questions

1. Should `--version` accept a `5.x` floating spec? Aspire doesn't. Recommend: no, exact version only.
2. Hash validation: the existing install scripts already compute and verify SHA-256 from the release notes. Confirm where the canonical hash list lives so the in-process updater reads the same source.