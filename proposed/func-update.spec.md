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

## Version discovery (CDN version manifest)

Version discovery uses a **version manifest** hosted on the Azure Functions CDN,
replacing the previous GitHub Releases API approach. This avoids GitHub API rate
limits and authentication requirements, making `func update` reliable in CI
environments and for unauthenticated users.

### Manifest URL

```
https://cdn.functions.azure.com/public/cli/v5/version.json
```

### Manifest format

`--prerelease` = latest stable or prerelease (include preview in the comparison,
pick whichever is higher by SemVer precedence).

Default (no flag) = latest stable only.

```json
{
  "stable": "5.3.0",
  "preview": "5.4.0-preview.1"
}
```

| Field | Description |
| --- | --- |
| `stable` | Latest stable release version (no prerelease label) |
| `preview` | Latest prerelease version (any prerelease label: `preview.N`, `rc.N`, `beta.N`, etc.) |

Both fields contain bare SemVer version strings (no `v` prefix).

### Download URL pattern

Artifacts are hosted on the same CDN at a predictable path:

```
https://cdn.functions.azure.com/public/cli/v5/{version}/Azure.Functions.Cli.{rid}.{version}.zip
```

Examples:

- `https://cdn.functions.azure.com/public/cli/v5/5.1.2/Azure.Functions.Cli.win-x64.5.1.2.zip`
- `https://cdn.functions.azure.com/public/cli/v5/5.2.0-preview.1/Azure.Functions.Cli.osx-arm64.5.2.0-preview.1.zip`

### Advantages over GitHub Releases

- **Better rate limits**: anonymous CDN access with far more generous limits
  than GitHub API (60 req/hr unauthenticated).
- **No authentication required**: works in CI, air-gapped proxies, and
  environments where GitHub tokens aren't available.
- **Predictable URLs**: download URLs are deterministic from version + RID,
  no need to enumerate release assets.
- **Faster discovery**: a single small JSON fetch vs paginated GitHub API calls.

## Channel resolution

Release quality over channel — the channel determines which versions are
eligible, not a separate concept from the version itself.

Two release channels, matching the existing install-script behaviour:

| Channel | Manifest field | Selector |
| --- | --- | --- |
| stable | `stable` | default |
| prerelease | `preview` | `--prerelease` |

Precedence:

1. `--version X.Y.Z` (explicit, wins; `--prerelease` is still allowed for clarity).
   Downloads directly from the CDN at the specified version without consulting
   the manifest.
2. `--prerelease` flag → reads `preview` from manifest.
3. Default → reads `stable` from manifest.

No per-project channel config.

### Version classification (SemVer)

Release classification uses [SemVer 2.0](https://semver.org/) on the version
string from the manifest:

| Version | SemVer parse | Channel |
| --- | --- | --- |
| `5.0.0` | no prerelease label | stable |
| `5.1.2` | no prerelease label | stable |
| `5.0.0-preview.1` | prerelease label | prerelease |
| `5.0.0-rc.1` | prerelease label | prerelease |

Rules:

- A version is **stable** iff `SemVersion.Prerelease` is empty.
- A version is **prerelease** iff `SemVersion.Prerelease` is non-empty.
- The running CLI's own version (from `AssemblyCliVersionProvider`) is parsed
  the same way, so "newer than current" comparisons use SemVer precedence.

We pick up [Semver](https://www.nuget.org/packages/Semver) (the library Aspire
uses) rather than `NuGet.Versioning`. It's a single small dependency that
implements SemVer 2.0 cleanly.

### Release channels vs deployment channels (future)

The `--prerelease` flag selects a **release channel** (which version to fetch
from the manifest). It is intentionally distinct from the **deployment channel**,
i.e. the install method the user originally used (GitHub install script, npm,
Homebrew, Chocolatey, winget, MSI).

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

## Update flow

```
GET https://cdn.functions.azure.com/public/cli/v5/version.json
  → parse manifest, select version from stable/preview field
  → compare with current version (SemVer precedence)
  → if newer: download zip from CDN at public/cli/v5/{version}/Azure.Functions.Cli.{rid}.{version}.zip
  → extract, swap, verify
```

## Install-method detection (must do, copy from Aspire)

`func update` MUST detect package-manager installs and **defer** to them instead
of overwriting their files.

Installation source is baked into the global state directory at
`~/.azure-functions/`. Only global installs are in scope; project-local installs
are out of scope for v1.

| Detected install | Behaviour |
| --- | --- |
| npm (`@azure/functions-core-tools` global) | Print `npm i -g @azure/functions-core-tools@<tag>` and exit 0 |
| Homebrew (`brew list azure-functions-core-tools`) | Print `brew upgrade azure-functions-core-tools` and exit 0 |
| Chocolatey (`choco`) | Print `choco upgrade azure-functions-core-tools` and exit 0 |
| MSI / winget | Print `winget upgrade …` and exit 0 |
| Install script (`~/.azure-functions/func`) | Perform in-place update (this spec) |

> **Open Q:** Do we want to print the upgrade command or try to run it ourselves?
>
> We discussed having a single install location of the CLI instead of different
> locations (like mentioned above per dist channel) to avoid having multiple
> versions of the CLI installed at the same time. If we do that, we probably
> should always update via script and not print out the dist channel upgrade
> command.

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
2. Resolve target version from CDN manifest (or `--version` directly).
3. Build download URL: `https://cdn.functions.azure.com/public/cli/v5/{version}/Azure.Functions.Cli.{rid}.{version}.zip`
4. Download the archive to a temp dir.
5. Extract to a temp dir.
6. Locate the new `func` / `func.exe` in the extracted payload.
7. Rename current binary to `func.old.<unixTimestamp>` (backup).
8. Copy new binary into `installDir`; on Unix set executable bit.
9. Probe `func --version` on the new binary; if it fails or returns garbage, roll back from backup.
10. On success, sweep old `.old.*` siblings (Aspire's `FileDeleteHelper.TryCleanupOldItems`).
11. If `installDir` is not on `PATH`, print the same "add to PATH" hint the install script prints.

Important deltas from Aspire:

- None substantive. `func` ships as a single self-contained binary
  (`func` / `func.exe`), so the swap is the same single-file rename Aspire
  performs: extract → backup current as `.old.<timestamp>` → copy new in place
  → `chmod +x` on Unix → verify `func --version` → sweep old backups
  (rollback on failure).
- Sibling state (e.g. `~/.azure-functions/workloads`) MUST NOT be touched;
  workloads are managed by `func workload install/update` and survive a CLI
  update. The swap only ever writes the `func` binary itself.

## Pipeline requirements (CDN publishing)

The release pipeline must publish artifacts to the CDN and update the version
manifest. The process uses a two-phase approach for atomicity:

### Phase 1: Upload versioned artifacts

Upload the platform-specific zip archives to the CDN:

```
public/cli/v5/{version}/Azure.Functions.Cli.{rid}.{version}.zip
```

For all supported RIDs:

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`
- `linux-x64`
- `linux-arm64`

All artifacts for a version MUST be uploaded and verified before proceeding to
Phase 2. This ensures that when a client reads a version from the manifest, the
corresponding artifact is guaranteed to be available.

### Phase 2: Upload install scripts

The install scripts (`install.ps1`, `install.sh`) are hosted on the CDN at:

```
public/cli/v5/install.ps1
public/cli/v5/install.sh
```

Install scripts MUST be **code-signed** before upload to CDN. The signing step
is part of the release pipeline and must complete before the scripts are
published.

### Phase 3: Update version manifest

After all artifacts are confirmed available, update:

```
public/cli/v5/version.json
```

Update rules:

- If the published version has no prerelease label → update the `stable` field.
- If the published version has a prerelease label → update the `preview` field.
- Both fields are always present in the manifest (even if unchanged).

### Atomicity guarantees

- **Artifacts before manifest**: clients will never see a version in the manifest
  that doesn't have downloadable artifacts. The manifest is updated last.
- **Manifest update is a single blob write**: CDN blob overwrites are atomic from
  the client's perspective (readers get either the old or new content, never a
  partial write).
- **Rollback**: to roll back a bad release, revert the manifest to the previous
  version. The old artifacts remain on CDN and can be re-promoted by updating
  the manifest again.

### CDN caching considerations

- The version manifest should have a short TTL (e.g. 5 minutes) so clients pick
  up new versions quickly.
- Versioned artifact blobs are immutable and can be cached aggressively
  (long TTL / indefinite).
- Cache drop strategy: purge all files under `public/cli/v5/` **except** for
  `{version}/` folders (which are immutable). The manifest and install scripts
  need cache invalidation on publish.
- Investigate: CDN API to programmatically drop cache for specific files, to
  integrate into release CI pipeline.

## Cancellation / errors

- All async paths take a `CancellationToken`; Ctrl+C cancels cleanly.
- HTTP errors (manifest fetch or artifact download) are caught at the command
  boundary and wrapped in `GracefulException(message, isUserError: true)` with
  actionable text (e.g. "Check network connectivity" on timeout, "Version not
  found" on 404).
- Filesystem errors are caught similarly (e.g. "Run with sudo" on
  `UnauthorizedAccessException`, "Re-run the install script" if `installDir`
  is not writable).
- Unrecognised exceptions surface as runtime bugs (stack trace), per repo
  conventions.

## Code layout (proposed)

```text
src/Func/Commands/Update/
  UpdateCommand.cs          # FuncCliCommand + IBuiltInCommand, thin handler
  UpdateCommandOptions.cs   # parsed options DTO
  UpdateRunner.cs           # orchestrates check → download → swap → verify

src/Func/Update/
  IReleaseFeed.cs           # CDN version manifest abstraction
  CdnReleaseFeed.cs         # IHttpClientFactory-backed impl (fetches version.json)
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
- `CdnReleaseFeedTests` — `HttpMessageHandler` stub returning fixture payloads; manifest parsing, stable vs prerelease field selection, direct version URL construction, 404 handling for unknown versions.

## Work items

Status of implementation relative to this spec.

### Done

| # | Item | Status |
| - | ---- | ------ |
| 1 | `UpdateCommand` scaffold (options, help text, Parser registration) | ✅ Done (PR #5297) |
| 2 | `IReleaseFeed` abstraction + `CdnReleaseFeed` implementation | ✅ Done (PR #5298) |
| 3 | `CdnReleaseFeedTests` (manifest parsing, channel selection, URL construction) | ✅ Done (PR #5298) |
| 4 | `VersionManifest` model + JSON serialization context | ✅ Done (PR #5298) |
| 5 | `ICliUpdater` download/swap/rollback pipeline | ✅ Done (PR #5428) |

### Remaining (CLI implementation)

| # | Item | Description |
| - | ---- | ----------- |
| 6 | `UpdateRunner` / command handler wiring | Orchestrates the full flow: resolve version → compare → download → swap → verify. Replaces the placeholder `GracefulException` in `ExecuteAsync`. |
| 7 | `IInstallMethodDetector` + `InstallMethodDetector` | Detect npm/brew/choco/winget/install-script; print appropriate upgrade command and exit for managed installs. |
| 8 | Confirmation prompt + `--yes` enforcement | Interactive prompt via `IInteractionService`; require `--yes` when non-interactive. |
| 9 | Progress reporting | Download progress bar / spinner via `IInteractionService`. |
| 10 | `UpdateCommand` integration tests | End-to-end with a fake HTTP server serving manifest + zip; verify binary swap and rollback. |

### Remaining (pipeline / infrastructure)

| # | Item | Description |
| - | ---- | ----------- |
| 11 | CDN artifact upload in release pipeline | Publish platform zips to `public/cli/v5/{version}/Azure.Functions.Cli.{rid}.{version}.zip` for all supported RIDs. |
| 12 | Install script signing + CDN upload | Sign `install.ps1` / `install.sh` and publish to `public/cli/v5/install.ps1` / `install.sh`. |
| 13 | Version manifest publish step | After all artifacts are uploaded and verified, write/update `public/cli/v5/version.json` with new stable or preview version. |
| 14 | CDN cache invalidation | Implement programmatic cache purge for `public/cli/v5/` non-versioned files (manifest, install scripts) in release CI. |
| 15 | Rollback runbook | Document how to revert a bad release by re-publishing the previous manifest. |

### Future (not blocking v1 of `func update`)

| # | Item | Description |
| - | ---- | ----------- |
| 16 | Update notifier (background check) | Periodic "a new version is available" notice on CLI startup. |
| 17 | Deployment-channel-aware update hints | npm/brew/choco/winget users see their native upgrade command. |
| 18 | SHA-256 verification | Publish a checksum file alongside zips; verify after download. |
