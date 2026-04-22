# Workload Spec

> **Status:** Draft. This spec is the **contract** that any workload model
> implementation must satisfy. It deliberately avoids prescribing
> in-process vs out-of-process — see [`workload-design.md`](./workload-design.md)
> for the comparison of those options.

## 1. Goals

- Allow the v5 `func` CLI to be extended with language- and feature-
  specific functionality without rebuilding or shipping a new CLI.
- Keep the CLI binary small and stable; defer language-specific work to
  workloads acquired on demand.
- Provide a uniform UX across all languages: a user runs the same
  `func init`, `func new`, `func pack` regardless of which workload backs
  the project.
- Allow the Functions team and external authors to ship workloads on their
  own cadence, independent of the Func CLI's release schedule.
- Make workload acquisition, update, and removal a first-class CLI
  experience (`func workload …`).

## 2. Non-goals (v1)

- **Sandboxing.** Workloads run with the same OS privileges as `func`. We
  rely on package provenance (signed NuGets) for trust, not runtime
  containment.
- **Cross-workload coordination.** Workloads do not call each other.
- **Authoring telemetry framework.** Workloads may emit logs/diagnostics;
  shared telemetry primitives are out of scope for v1.
- **GUI / IDE integration.** This spec covers the CLI only. IDE
  integrations consume `func` as a black box.
- **Workload-supplied UI prompts.** v1 keeps interactive prompts on the
  CLI side; the workload returns data, the CLI renders.

## 3. Definitions

| Term                  | Meaning                                                                 |
|-----------------------|-------------------------------------------------------------------------|
| **Func CLI**          | The `func` binary itself. Owns command parsing, UX, workload lifecycle. Distinct from the Functions host runtime, which is itself delivered as a workload (see §4.6). |
| **Host runtime**      | `Microsoft.Azure.WebJobs.Script.WebHost` — the process that actually executes Functions. Acquired and versioned as a workload, launched by `func start`. |
| **Workload**          | An installable extension contributing init/new/pack support and/or commands. |
| **Workload package**  | The distributable unit. NuGet in v1; format may evolve.                 |
| **Workload manifest** | Per-workload metadata describing id, version, capabilities, etc.         |
| **Worker runtime**    | The Functions runtime language identifier (`dotnet`, `node`, `python`, `java`, `powershell`, `custom`). |
| **Capability**        | A named feature a workload supports (e.g. `project.init`, `templates`, `pack`). |
| **Project marker**    | A glob/file pattern (`*.csproj`, `package.json`, …) that hints which workload owns a given directory. |

## 4. User-facing requirements

### 4.1 `func workload` — package management

| Command                                | Behavior                                                          |
|----------------------------------------|-------------------------------------------------------------------|
| `func workload list`                   | List installed workloads with id, version, capabilities, install path. |
| `func workload search [<query>]`       | Search the configured workload catalog (NuGet feed). Returns id, latest version, description. |
| `func workload install <id> [--version <v>] [--from <path>]` | Acquire and install a workload. `--from` installs from a local path/feed for development. |
| `func workload uninstall <id> [--all-versions]` | Remove an installed workload. By default removes only the active version. |
| `func workload update [<id>] [--all]` | Update one or all workloads to the latest compatible version.     |

**Required behaviors:**

- All commands must be **non-interactive** when given complete arguments and
  must exit with a non-zero code on failure.
- `install`, `uninstall`, `update` must be **idempotent**: re-running with
  the same effective state must succeed without error.
- A failed install must leave no partial state on disk (atomic move into
  the install root, or rollback).
- Concurrent invocations must not corrupt the workload store. (Cross-
  process locking on the install root.)

### 4.2 `func init` — scaffold a project

- If no workload is installed for the requested runtime, the Func CLI **must**
  print an actionable hint with the exact command to install one (e.g.
  `func workload install python`) and exit with a clear error.
- If a workload is installed, the Func CLI delegates project scaffolding to it.
- If `--worker-runtime` is omitted, the Func CLI **may** prompt or auto-select
  based on installed workloads. Behavior must be consistent across
  workloads.

### 4.3 `func new` — create a function from a template

- Templates come **only** from installed workloads. The Func CLI has no
  built-in templates.
- `func new` (no args) lists templates from every installed workload.
- `func new --template <name> --name <fn>` materializes the template
  identified by name. If multiple workloads expose the same template name,
  the Func CLI disambiguates by `--worker-runtime` or via an error listing the
  conflicts.

### 4.4 `func pack` — build & package for deployment

- The Func CLI inspects the current directory and asks each candidate workload
  (filtered by project markers, then by `project.detect`) whether it owns
  the directory.
- Exactly one workload must claim the directory. Zero → error with install
  hint. More than one → error listing the contenders, suggest
  `--worker-runtime` to disambiguate.
- Pack output paths and naming are workload-defined; the Func CLI surfaces the
  resulting artifact path back to the user.

### 4.5 `func start` and other built-in commands

- `func start` launches the Functions **host runtime**. The Func CLI **must
  not** embed the host runtime in its own binary; the runtime is acquired
  and updated as a workload (see §4.6) so it can ship and version
  independently of the CLI.
- In v1, `func start` resolves the host-runtime workload, ensures it is
  installed (offering to install if missing), and delegates process
  startup to it. Argument forwarding, port selection, and log streaming
  remain CLI responsibilities.
- Workloads **may** register additional command trees (e.g., `func durable
  …`). The Func CLI must surface these in `--help` output alongside built-in
  commands and label the source workload in verbose help.

### 4.6 Host runtime as a workload

- The Functions host runtime is distributed as a first-class workload
  (e.g. `Azure.Functions.Cli.Workload.Host`). It is **not** bundled
  with the CLI binary.
- The host-runtime workload contributes a `host.run` capability (analogous
  to the language workload capabilities in §5) that `func start` invokes.
- Multiple host-runtime versions **may** coexist; the CLI selects one per
  invocation based on (in order): `--host-version` flag, project pin
  (e.g. `host.json` / project file metadata), latest installed.
- All other workload contracts (manifest, lifecycle, update, telemetry)
  apply unchanged. There is no special-case install path for the host
  runtime.
- This separation lets host hotfixes ship without a CLI release and lets
  the CLI itself be smaller and AOT-friendly.

## 5. Workload contract — what a workload provides

A workload **may** implement any subset of the following capabilities. It
**must** declare which it supports in its manifest.

| Capability        | Method / interface                                  | Required for                     |
|-------------------|-----------------------------------------------------|----------------------------------|
| `project.detect`  | "Does this directory look like my project?"         | `func pack` directory routing    |
| `project.init`    | Scaffold a new project                              | `func init`                      |
| `templates.list`  | Enumerate available templates                       | `func new` (listing)             |
| `templates.create`| Materialize a template into the project             | `func new --template`            |
| `pack.run`        | Build & package the project                         | `func pack`                      |
| `commands`        | Register additional CLI commands                    | Feature workloads (e.g. durable) |

### 5.1 Manifest requirements

Every workload package **must** ship a manifest declaring at least:

| Field              | Type     | Required | Notes                                            |
|--------------------|----------|----------|--------------------------------------------------|
| `schemaVersion`    | int      | yes      | Manifest schema version. v1 = `1`.               |
| `id`               | string   | yes      | Unique, kebab-case (e.g. `python`, `dotnet`).     |
| `version`          | semver   | yes      | Workload version (independent of Func CLI version). |
| `displayName`      | string   | yes      | Shown in `func workload list`.                   |
| `description`      | string   | yes      | One-line summary.                                |
| `workerRuntimes`   | string[] | yes      | Worker runtime ids this workload owns.           |
| `languages`        | string[] | no       | Display labels (e.g. `["C#", "F#"]`).             |
| `capabilities`     | string[] | yes      | Subset of capabilities listed above.             |
| `projectMarkers`   | string[] | no       | Glob patterns for fast pre-filtering in `pack`.  |
| `protocolVersion`  | string   | impl-dependent | Required for out-of-process model (Option B). |
| `executable`       | string   | impl-dependent | Required for out-of-process model.            |

Implementation-specific fields (e.g. `executable`, assembly name) are
documented in the chosen design.

### 5.2 Project detection

- Pre-filter: the Func CLI applies `projectMarkers` (if present) to the target
  directory before invoking the workload. Workloads with no markers are
  always candidates.
- Detection: the workload returns a confidence (`yes` / `no` / `maybe`)
  plus an optional reason string.
- Tie-breaking: if multiple workloads return `yes`, the Func CLI errors with
  the list and asks the user to disambiguate via `--worker-runtime`.

### 5.3 Versioning

- Workloads use **semver**. `func workload update` uses the same major
  version by default; major bumps require an explicit opt-in.
- The Func CLI declares a **minimum supported manifest schema version** and
  rejects workloads below it with a clear error pointing at
  `func workload update`.

## 6. Lifecycle

### 6.1 Install

1. Resolve `<id>` to a package via the catalog (NuGet by default).
2. Download to a staging directory.
3. Validate the manifest (required fields, schema version).
4. Atomically move into `~/.azure-functions/workloads/<id>/<version>/` (or
   the equivalent path for the chosen design).
5. Emit a "installed" message including version and capabilities.

### 6.2 Discovery

- On every Func CLI startup, scan the install root for manifests.
- Discovery **must** complete in O(workload count) and **must not** spawn
  any workload process or load any workload assembly during scan (Option B
  may defer process spawn until first invocation; Option A may defer
  assembly load until first request).

### 6.3 Invocation

- The Func CLI binds the relevant request (init/new/pack/etc.) to the
  capability declared in the manifest.
- Errors from a workload **must** surface to the user with the workload id
  prefixed (e.g. `[python] error: ...`).
- The Func CLI **must** distinguish between workload-protocol errors (bug, ask
  user to file an issue) and user-facing errors (display verbatim).

### 6.4 Update

- `func workload update <id>` resolves a newer version, installs it
  side-by-side, then atomically swaps the "active" version pointer.
- Old versions remain on disk until `--prune` is passed, allowing rollback.

### 6.5 Removal

- `func workload uninstall <id>` removes the active version's directory.
- `--all-versions` removes every installed version.
- Other installed workloads must be unaffected.

### 6.6 Background staleness check

- The Func CLI **may** periodically check the catalog for newer versions of
  installed workloads and surface a one-line hint (`A new version of
  python (1.2.3) is available. Run 'func workload update python' to
  upgrade.`).
- Frequency: at most once per 24h. Network failures must be silent.
- Disabled by `FUNCTIONS_DISABLE_WORKLOAD_UPDATE_CHECK=1`.

## 7. Error handling requirements

| Scenario                                    | Required behavior                                                                                  |
|---------------------------------------------|----------------------------------------------------------------------------------------------------|
| No workload installed for runtime           | Print exact install command, exit non-zero.                                                       |
| Workload manifest invalid                   | Skip the workload during discovery, log a warning to stderr, do not crash.                        |
| Workload fails during request               | Surface the error with `[<workload-id>]` prefix; exit non-zero with the workload's exit code (or 1). |
| Workload protocol violation (Option B)      | Treat as a workload bug; print actionable error, exit non-zero.                                   |
| Workload spawn / load failure               | Print the underlying error and the install path; suggest `func workload uninstall && install`.   |
| Two workloads claim same project / template | Error listing all candidates, suggest `--worker-runtime` to disambiguate.                         |
| Catalog unreachable                         | `install`/`update` must error clearly; `list` and offline operations must still work.             |

## 8. Performance requirements

- **CLI startup overhead** for workload discovery (no invocation) must
  remain under **100 ms** for up to 25 installed workloads on a warm cache.
- **First invocation** of a workload (cold) should complete the
  workload-side boot in under **300 ms** for AOT'd workloads and
  **800 ms** for JIT.
- **Subsequent invocations** within the same `func` invocation should not
  re-pay boot cost when the Func CLI can reuse the workload connection (an
  optimization, not a v1 requirement).

## 9. Security & trust

- v1 trusts NuGet package provenance. Workloads acquired from non-default
  feeds must be flagged in `func workload list` (`source: <feed>`).
- The Func CLI **must not** execute any workload code during discovery. Any
  code execution path must require explicit user action (install, init,
  new, pack, custom command).
- Workloads run with the user's privileges. We document this clearly in
  `func workload install --help`.

## 10. Compatibility & versioning

- The Func CLI advertises a **manifest schema version** and **(if applicable)
  protocol version** during workload invocation.
- Workloads built against a newer schema or protocol than the Func CLI
  supports must be rejected with a "Func CLI too old, please update" message.
- Workloads built against an older schema/protocol must continue to work
  if the Func CLI can satisfy the older contract; otherwise the Func CLI rejects
  with a "workload too old, please run `func workload update`" message.

## 11. Telemetry expectations

- The Func CLI emits anonymous telemetry per command invocation including:
  `command`, `workload-id` (when one was selected), `outcome`
  (success / user-error / workload-error / cli-error), `duration`.
- Workloads **may** emit their own telemetry but **must not** propagate
  user-identifying data through the Func CLI.
- Telemetry is opt-out via the existing `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT`
  environment variable.

## 12. Open questions

1. **Catalog source** — single NuGet feed by default, or multi-feed with
   precedence rules?
2. **Workload signing** — do we require signed packages for v1, or only
   warn?
3. **Cross-RID workloads** — does `func workload install python` resolve
   per-RID artifacts, or does the workload bundle all RIDs?
4. **Process reuse** (Option B specific) — within a single `func` invocation
   that fires multiple workload calls, do we reuse the spawn?
5. **Bidirectional UX** (logs, progress, prompts) — CLI-rendered only in
   v1, or do we lock in a callback contract now to avoid breaking changes
   later?
6. **Built-in workload** — does the CLI ship with any workload pre-installed
   (e.g. seed the host-runtime workload on first run), or is the post-install
   state always "no workloads" and `func start` triggers an install prompt?
7. **Host-runtime version pinning** — where does the per-project pin live
   (`host.json`, `local.settings.json`, a new `func.json`, project file)?
8. **Host-runtime workload boundary** — does one workload ship all RIDs +
   all major host versions, or one workload per major (e.g. `…Workload.Host.v4`,
   `…Workload.Host.v5`)?
