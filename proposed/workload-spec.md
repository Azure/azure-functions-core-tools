# Workload Spec

> **Status:** Draft. This spec defines the contract between the Func CLI
> and workload packages. v1 uses an **in-process** model: workloads are
> .NET assemblies loaded into the `func` process inside isolated
> collectible `AssemblyLoadContext`s.

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
  experience (`func workload ...`).

## 2. Non-goals (v1)

- **Sandboxing.** Workloads run with the same OS privileges as `func`. We
  rely on package provenance (NuGet feed trust, signing where available)
  for trust, not runtime containment. Whether v1 *requires* signed
  packages or only warns is an open question (see §12).
- **Cross-workload coordination.** Workloads do not call each other
  directly. The Func CLI brokers the only legitimate cross-workload
  interaction: invoking the host-runtime workload (§4.6) on behalf of a
  language workload during `func start`.
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
| **Host runtime**      | `Microsoft.Azure.WebJobs.Script.WebHost`, the process that actually executes Functions. Acquired and versioned as a workload, launched by `func start`. |
| **Workload**          | An installable extension that contributes init/new/pack support and/or commands. Concretely: a NuGet package whose entry-point assembly contains a class deriving from the abstract `Workload` base in `Func.Cli.Abstractions` (see §5). |
| **Workload package**  | The distributable unit. NuGet in v1; format may evolve.                 |
| **Catalog**           | The package source `func workload search` / `install` queries to resolve workload packages. The configured default is the public NuGet feed; alternates can be passed via `--source`. |
| **Alias**             | A short, NuGet-tag-safe handle (e.g. `python`) declared by a workload package via an `alias:<name>` tag. Users can pass aliases to `install` / `uninstall` / `update` instead of the full package id (see §5.3, §6.1). |
| **Workload home**     | The on-disk root the CLI uses to install workloads and read the workload registry. Defaults to `~/.azure-functions`; configurable via the `FUNC_CLI_Workloads__Home` environment variable (see §11 for the env var prefix convention). |
| **Workload registry**   | The single CLI-owned JSON file at `<workload-home>/workloads.json` recording which workload versions are installed and how to load them. |
| **Worker runtime**    | The Functions runtime language identifier (`dotnet`, `node`, `python`, `java`, `powershell`, `custom`). |
| **Contribution point** | An interface in `Func.Cli.Abstractions` (e.g. `IProjectInitializer`) that a workload registers to participate in a built-in command. |
| **Project marker**    | A glob or file pattern (`*.csproj`, `package.json`, ...) that hints which workload owns a given directory. |

## 4. User-facing requirements

### 4.1 `func workload` — package management

#### `func workload list`

```
func workload list [--all-versions|-a] [--json]
```

Lists installed workloads with id, version, capabilities, and install path.

##### Options

- `--all-versions|-a`
  - Lists every installed version of every workload. Default: only the
    active version per workload is shown.
- `--json`
  - Emits machine-readable JSON output instead of the default
    human-readable table. Required for scripting.

#### `func workload search`

```
func workload search [<query>] [--source <path>] [--include-prereleases] [--json]
```

Searches the configured workload catalog (NuGet feed by default) and
returns id, latest version, and description for matching packages.

##### Arguments

- `<query>`
  - Optional free-form search string. When omitted, returns all packages
    in the catalog declaring the `FuncCliWorkload` package type.

##### Options

- `--source <path>`
  - Queries an alternate feed (URL or local path) instead of the
    configured default catalog.
- `--include-prereleases`
  - Includes pre-release versions in the results. Default: stable only.
- `--json`
  - Emits machine-readable JSON output.

#### `func workload install`

```
func workload install <package> [--version|-v <v>] [--source <path>] [--exact|-e] [--include-prereleases]
```

Acquires and installs a workload. See §6.1 for the full install pipeline.

##### Arguments

- `<package>`
  - Required. Matched as an `alias:<name>` tag by default (see §5.3 and
    §6.1 for the resolution flow). With `--exact`, must be the literal
    package id.

##### Options

- `--version|-v`
  - Installs the specified semver version. Default: the latest stable
    version available in the catalog. Combine with
    `--include-prereleases` to allow pre-release versions when resolving
    "latest".
- `--source <path>`
  - Installs from a local path or alternate feed (e.g. for development
    or internal mirrors) instead of the configured default catalog.
- `--exact|-e`
  - Disables alias matching. `<package>` must be the literal package id
    (case-insensitive). Use when an alias collides across multiple
    packages or when scripting against a known id.
- `--include-prereleases`
  - Allows pre-release versions to be selected when resolving "latest".
    Default: stable only.

#### `func workload uninstall`

```
func workload uninstall <package> [--version|-v <v>] [--all-versions|-a] [--exact|-e]
```

Removes an installed workload. By default removes only the active version.

##### Arguments

- `<package>`
  - Required. The workload to remove. Resolved using the same alias
    rules as `install` (see §6.1).

##### Options

- `--version|-v`
  - Removes a specific installed version. Useful for cleaning up old
    side-by-side installs without affecting the active version.
- `--all-versions|-a`
  - Removes every installed version of the workload. Mutually exclusive
    with `--version`.
- `--exact|-e`
  - Disables alias matching. `<package>` must be the literal package id
    (case-insensitive). Use when an alias collides across multiple
    installed packages or when scripting against a known id.

#### `func workload update`

```
func workload update [<package>] [--all] [--major] [--prune] [--source <path>] [--include-prereleases]
```

Updates one or all workloads to the latest compatible version. Default
is "same major version only". The new version is installed side-by-side
and the active pointer is swapped atomically (see §6.4).

##### Arguments

- `<package>`
  - Optional. Updates a single workload. Mutually exclusive with
    `--all`. Omitting both `<package>` and `--all` is an error.

##### Options

- `--all`
  - Updates every installed workload. Mutually exclusive with `<package>`.
- `--major`
  - Allows crossing a major-version boundary. Default: same major
    version only, to protect against breaking changes.
- `--prune`
  - Deletes superseded versions from disk after a successful swap.
    Default: keep prior versions to allow rollback (see §6.4).
- `--source <path>`
  - Resolves updates from an alternate feed instead of the configured
    default catalog.
- `--include-prereleases`
  - Allows pre-release versions to be selected when resolving "latest".
    Default: stable only.

#### `func workload pin`

```
func workload pin <package> --version|-v <v> [--exact|-e]
```

Pins an installed workload to a specific version. While pinned, the
loader honors `<v>` regardless of newer installs (see §6.2
"Multiple-version policy"). The pin is persisted to the workload
registry (§10.1) under `activeVersions[<packageId>]`, so it survives
across `func` invocations.

Use this to roll forward, roll back, or hold a workload at a known-good
version while you investigate a regression. The named version **must**
already be installed; if it isn't, the CLI errors with the exact
`func workload install` command to run.

This is a registry-only operation: no catalog round-trip, no
re-download. The change is durable on disk the moment the command
returns; it takes effect on the **next** `func` invocation. The
currently running CLI process keeps the workloads it discovered at
startup (§6.2 step 8); it does not reload them mid-invocation.

##### Arguments

- `<package>`
  - Required. The workload to pin. Resolved using the same alias rules
    as `install` (see §6.1).

##### Options

- `--version|-v`
  - **Required.** The semver to pin to. Must already be installed.
- `--exact|-e`
  - Disables alias matching. `<package>` must be the literal package id
    (case-insensitive).

#### `func workload unpin`

```
func workload unpin <package> [--exact|-e]
```

Removes any pin on the named workload, reverting it to the default
behavior: the loader picks the highest installed semver. The new `func
workload install` and `update` runs will be picked up automatically on
the next invocation.

Like `pin`, this is a registry-only write and takes effect on the next
`func` invocation. Unpinning a workload that wasn't pinned is a no-op
and exits success.

##### Arguments

- `<package>`
  - Required. The workload to unpin.

##### Options

- `--exact|-e`
  - Disables alias matching. `<package>` must be the literal package id
    (case-insensitive).

#### Required behaviors (all `func workload` commands)

- All commands must be **non-interactive** when given complete arguments
  and must exit with a non-zero code on failure.
- `install`, `uninstall`, `update` must be **idempotent**: re-running
  with the same effective state must succeed without error.
- A failed install must leave no partial state on disk (atomic move
  into the install root, or rollback).
- Concurrent invocations must not corrupt the workload store
  (cross-process locking on the install root).

### 4.2 `func init` — scaffold a project

- If no workload is installed for the requested runtime, the Func CLI **must**
  print an actionable hint with the exact command to install one (e.g.
  `func workload install python`) and exit with a clear error.
- The CLI determines the suggested workload using, in order:
  1. An explicit `--stack <name>` flag, if provided.
  2. `FUNCTIONS_WORKER_RUNTIME` from `local.settings.json`, if present.
  3. Lightweight project-marker detection (e.g. presence of `*.csproj`,
     `requirements.txt`, `package.json`) to make a best-effort guess.
- This fallback applies only when no workload is installed for the
  requested runtime. When workloads *are* installed, §5.2 (Workload
  resolution) describes the richer detector-based flow.
- If detection is ambiguous or yields no signal, the hint **must** list the
  canonical install command for each known stack rather than guessing.
- If a workload is installed, the Func CLI delegates project scaffolding to it.

### 4.3 `func new` — create a function from a template

- Templates come **only** from installed workloads. The Func CLI has no
  built-in templates.
- `func new` (no args) lists templates from every installed workload that
  matches the **current project's stack**. Stack resolution follows
  §5.2 (Workload resolution).

  If no stack can be resolved, `func new` lists templates from every
  installed workload and prompts the user (or errors in non-interactive
  mode) to disambiguate via `--stack`.
- `func new --template <name> --name <fn>` materializes the template
  identified by name. If multiple workloads expose the same template name,
  the Func CLI disambiguates by `--stack` or via an error listing the
  conflicts.

### 4.4 `func pack` — build & package for deployment

- The owning workload is selected via §5.2 (Workload resolution).
  Exactly one workload must claim the directory. Zero is an error with an
  install hint. More than one is an error listing the contenders,
  suggesting `--stack` to disambiguate.
- The owning workload performs the build and package via its
  `IPackProvider` contribution (§5.1). Pack output paths and naming are
  workload-defined; the Func CLI surfaces the resulting artifact path
  back to the user.

### 4.5 `func start` and other built-in commands

- `func start` launches the Functions **host runtime**. The Func CLI **must
  not** embed the host runtime in its own binary; the runtime is acquired
  and updated as a workload (see §4.6) so it can ship and version
  independently of the CLI.
- In v1, `func start` resolves the host-runtime workload and delegates
  process startup to it. If the workload is not installed, the Func CLI
  **must** print the install hint
  (`Run 'func workload install <package>' to install the host runtime.`)
  and exit non-zero; it **must not** auto-install or prompt. Argument
  forwarding, port selection, and log streaming remain CLI responsibilities.
- Workloads **may** register additional command trees via
  `IExternalCommand` (§5.1), e.g. `func durable ...`. The Func CLI must
  surface these in `--help` output alongside built-in commands and label
  the source workload in verbose help.

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

### 4.7 v4 → v5 migration hints

Many subcommands that lived in the v4 monolith (`func azure ...`,
`func kubernetes ...`, `func durable ...`, etc.) move into workloads in
v5. Users with v4 muscle memory will still type the old commands, and
without help they will see a generic "unknown command" error.

- The Func CLI **must** maintain a curated map of known v4 subcommands
  to the workload package id that now provides them.
- When a user invokes one of these on v5 without the corresponding
  workload installed, the CLI prints (and exits non-zero):
  `'<cmd>' is now provided by a workload. Install it with 'func workload install <package>'.`
- The map covers any v4 surface that has moved, not only `func init` /
  `func new`; it applies to all unknown-subcommand paths.
- The map is **CLI-internal**: workloads do not contribute to it. New
  v4-migration entries ship in a CLI release; this is acceptable
  because the v4 surface is closed.

## 5. Workload contract — what a workload provides

A workload is a NuGet package containing a .NET assembly with a class
that extends the abstract `Workload` base from `Func.Cli.Abstractions`:

```csharp
public sealed class MyWorkload : Workload
{
    public override string Name => "Azure.Functions.Cli.Workload.MyStack";
    public override string DisplayName => "My Stack";
    public override string Description => "Functions support for My Stack.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        // register IProjectInitializer, ITemplateProvider, etc.
    }
}
```

`Version` defaults to the workload assembly's
`AssemblyInformationalVersion` (falling back to `AssemblyFileVersion` /
`AssemblyName.Version`), so most workloads can let the build supply
the version. Override only when the running code should be the source
of truth.

`Name` is the identity the workload **claims for itself** in code. The
CLI uses it as the prefix on diagnostic messages (`[<Name>] ...`) and as
the `workload-id` field in telemetry (see §11). It is conventionally
the assembly / package name (e.g.
`"Azure.Functions.Cli.Workload.Dotnet"`) and normally matches the
package id, but the two are distinct concepts:

- **`Workload.Name`** — declared by the workload code. Stable across
  republishes; what the workload calls itself in logs and telemetry.
- **`packageId`** — the NuGet identity used by `func workload install` /
  `uninstall` / `list` and recorded in the workload registry. What the
  user types.

They normally agree, but a workload republished under a new package id
(e.g. an ownership transfer) can keep its `Name` stable so log filters,
telemetry dashboards, and error-prefix grep patterns continue to work.

The package also ships a `workload.json` at the package root pointing
at the entry-point type; see §5.3 for the file shape and §6.1 for how
the install pipeline consumes it.

The `Workload` instance receives a `FunctionsCliBuilder` during host
startup (`Configure`) and registers concrete services into DI. The CLI
consumes those services via `IEnumerable<T>` collections and dispatches
based on which workload contributed which service. There is **no static
capability list**: what a workload can do is whatever it has registered.

### 5.1 Contribution points (DI)

A workload **may** register any subset of these services. Each service
the workload registers means it participates in the corresponding command:

| Service registered                | The workload can...                                          | Used by                          |
|-----------------------------------|-----------------------------------------------------------|----------------------------------|
| `IProjectInitializer`             | Scaffold a new project for a given worker runtime.        | `func init`                      |
| `IProjectDetector`                | Decide whether a directory is "its" project.              | `func init`, `func new`, `func pack`, `func start` (workload resolution) |
| `ITemplateProvider`               | Enumerate and materialize templates.                      | `func new`                       |
| `IPackProvider`                   | Build & package the project.                              | `func pack`                      |
| `IExternalCommand`                | Register additional CLI subcommands.                      | Any `func <subcommand>` contributed by a workload (e.g. `func durable ...`) |
| `IHostRuntimeLauncher`            | Launch the Functions host runtime process.                | `func start` (host workload)     |

Implementation status: as of this spec revision, only `IProjectInitializer`
exists in `Func.Cli.Abstractions`. The other contribution points are
designed but not yet implemented; they ship incrementally as the
corresponding built-in commands move onto the workload model.

The exact interface names and shapes are documented in
[`building-a-workload.md`](./building-a-workload.md). That document is
currently stale and will be refreshed alongside the first contract
revision; detailed interface signatures are deliberately out of scope
for this spec. Adding a new contribution point is an additive change
to `Func.Cli.Abstractions` (no manifest field to coordinate, no schema
bump).

### 5.2 Workload resolution

For commands that act on an existing project (`new`, `pack`, `start`),
the CLI must determine which installed workload owns the current
directory. Resolution proceeds in this order:

1. **Explicit selector.** A `--stack <name>` flag wins.
2. **`FUNCTIONS_WORKER_RUNTIME`** in `local.settings.json`, mapped to
   the workload that registered itself for that runtime. A workload
   declares the runtime ids it claims via metadata on its
   `IProjectDetector` registration (or, for workloads that ship no
   detector, via a property on the `Workload` subclass).
3. **`IProjectDetector`.** The CLI invokes every registered detector
   against the working directory:
   - **Pre-filter:** the CLI applies project-marker globs (declared by
     the workload's `IProjectDetector` registration) before invoking
     the detector. Workloads with no markers are always candidates.
   - **Detection:** the workload's `IProjectDetector` returns a
     confidence (`yes` / `no` / `maybe`) plus an optional reason string.
   - **Tie-breaking:**
     - Exactly one `yes` → that workload owns the command.
     - Zero matches → the CLI prints an actionable error.
     - Multiple `yes` results → the CLI errors with the list and asks
       the user to disambiguate via `--stack`.

`func init` is special: there is no project to detect against, so it
uses only steps 1 and 2 (selector → runtime). Steps 3+ require an
existing project.

`IProjectDetector` is therefore **not** specific to `pack`; it is the
shared mechanism for any command that needs to bind to a workload from
a directory. The §5.1 table reflects this.

### 5.3 Package metadata

The metadata the CLI needs is split between two sources:

1. **NuGet metadata (`.nuspec`)** — owned by the package author, captured
   at install time:
    - `id` → package id (NuGet rules apply, case-insensitive).
    - `version` → semver, the workload version.
    - `title` → display name shown in `func workload search` (catalog
      browse). The runtime `Workload.DisplayName` is the source of
      truth for `func workload list` once the package is installed.
    - `description` → one-line summary shown in `func workload search`.
      The runtime `Workload.Description` is the source of truth for
      installed workloads.
    - `tags` → NuGet tags. The CLI gives meaning to one reserved tag
      convention; all other tags are treated as free-form metadata for
      search ranking only:
        - `alias:<name>` → declares a short alias (e.g. `alias:python`)
          that the user can pass to `install` / `uninstall` / `update`
          instead of the full package id. A workload may declare
          multiple aliases. Aliases must be lowercase and
          NuGet-tag-safe; uniqueness is by convention only (NuGet has
          no central authority). Alias collisions across packages force
          users into `--exact` resolution (see §6.1 install flow).
    - `packageTypes` → **must** include a `FuncCliWorkload` entry. This
      is how the CLI (and the catalog) distinguish workload packages
      from arbitrary NuGets. Packages without this package type are
      rejected at install time and excluded from `func workload search`
      results. Modeled on the .NET CLI's `DotnetTool` package type
      (e.g. `?packageType=dotnettool` on the NuGet search API).
2. **Per-workload manifest (`workload.json`)** — owned by the package
   author, shipped at the root of the workload's NuGet package. Required:
   a package without this file is not a valid workload. Identifies the
   entry-point type the CLI activates after install:

   ```json
   {
     "entryPoint": {
       "assemblyPath": "Foo.dll",
       "type": "Foo.MyWorkload"
     },
     "minCliVersion": "5.0.0"
   }
   ```

   - `entryPoint.assemblyPath` → path to the assembly relative to the
     package's content root (see "Package layout" below). Must not be
     absolute or contain `..` segments; the install pipeline rejects
     either.
   - `entryPoint.type` → fully-qualified type name extending the
     abstract `Workload` base class.
   - `minCliVersion` (optional) → minimum Func CLI semver this workload
     requires. The loader rejects the workload with a "Func CLI too
     old, please update" error if the running CLI is older (see §10.2).

   The install pipeline reads `workload.json` once and records the
   entry-point spec in the workload registry, so subsequent CLI
   invocations don't have to crack the package open again.

   **Package layout.** A workload `.nupkg` follows this convention:

   ```
   <package-root>/
     workload.json              ← required, at the package root
     tools/any/
       <Workload>.dll           ← entryPoint.assemblyPath is resolved
       <dependencies>.dll          relative to tools/any/
       ...
   ```

   `tools/any/` is the content directory the install pipeline extracts
   and from which `entryPoint.assemblyPath` is resolved. The `tools/`
   prefix mirrors the .NET CLI's `dotnet tool` package convention
   (which uses `tools/<TFM>/any/`); workloads use a single `any`
   subdirectory because the CLI loads them in-process via
   `AssemblyLoadContext` rather than launching a separate runtime.

The CLI persists everything it needs for startup in a single
**workload registry** at `~/.azure-functions/workloads.json` (see §6.1).
Workload authors never touch this file; it is owned by `func workload
install` / `uninstall`.

### 5.4 Versioning

- Workloads use **semver**. `func workload update` uses the same major
  version by default; major bumps require an explicit opt-in.
- The CLI declares a **minimum supported registry schema version**
  (see §10) and rejects registries it cannot parse.

## 6. Lifecycle

### 6.1 Install

0. **Idempotency.** If the same `(packageId, version)` is already
   present in the workload registry and its install directory is
   intact, exit success without re-fetching or re-extracting. This
   makes `func workload install` safe to re-run.
1. Resolve `<package>` to a package via the catalog (NuGet by default),
   filtered to packages declaring the `FuncCliWorkload` package type:
   - **Alias path** (default): the resolver queries the catalog for
     packages whose tags include `alias:<package>`.
       - Exactly one match → install it.
       - Zero matches → fall back to exact-match-by-id. If that also
         finds nothing, fail with an actionable error listing close
         matches.
       - Multiple matches → fail and list the matched package ids;
         the user must re-run with `--exact <packageId>`.
   - **Exact path** (`--exact` / `-e`): the resolver targets exactly
     `<package>` as a literal package id (case-insensitive). No alias
     matching is performed. Fails if no such package exists in the
     catalog.
2. Download to a staging directory.
3. Read NuGet metadata (`id`, `version`, `title`, `description`, `tags`,
   `packageTypes`). Reject the package if `FuncCliWorkload` is not
   among its declared package types.

   Workloads ship **self-contained** under `tools/any/` (see §5.3
   "Package layout"); the install pipeline does **not** restore
   transitive NuGet dependencies. Anything the workload needs at
   runtime must be in the package.
4. Read `workload.json` from the package root. Reject if the file is
   missing, malformed, or `entryPoint.assemblyPath` is rooted or
   contains `..` segments.
5. Atomically move into
   `<workload-home>/workloads/<packageId>/<version>/`.
   `<packageId>` is stored lowercased (matching NuGet's normalization)
   so case differences in user input always resolve to the same
   directory.
6. Append an entry to the workload registry
   `<workload-home>/workloads.json` under the top-level `workloads`
   array. Each entry contains:
   - `packageId` — NuGet package id (lowercased; case-insensitive match
     on lookup).
   - `packageVersion` — installed version (ordinal match).
   - `aliases` — short aliases extracted from the package's
     `alias:<name>` tags.
   - `source` — the catalog feed URL or local path the package was
     installed from. Used to flag non-default-feed packages in
     `func workload list` (see §9).
   - `entryPoint: { assemblyPath, type }` — copied from the package's
     `workload.json` so the loader doesn't re-read the package on every
     CLI invocation.

   Side-by-side installs of the same package id at different versions
   are separate entries. `displayName` and `description` are **not**
   persisted in the registry; they are read from the loaded `Workload`
   instance at runtime so an updated workload's metadata always reflects
   what's running. Install paths are computed from the configured
   workload home plus `(packageId, packageVersion)` and likewise not
   persisted.

   The write is atomic (temp file + rename) so a crash mid-install
   cannot corrupt the registry.
7. **Update the registry pointer if needed.** A workload is unpinned by
   default: `install` does **not** set `activeVersions[<packageId>]`,
   so the loader resolves to the highest installed semver
   automatically. If the package id is currently *pinned* (because a
   prior `func workload use --version` set
   `activeVersions[<packageId>]`), `install` leaves the pin alone and
   prints a hint: `Run 'func workload use <package> --version <v>' to
   switch.` The user changes pins explicitly via `func workload use`
   (§4.1), never as a side effect of an install.

   To roll back to an older installed version, run
   `func workload use <package> --version <older>`. Combined with the
   step 0 idempotency, this means rollback is a pointer flip with no
   catalog round-trip.
8. Emit an "installed" message including version and entry-point type.

### 6.2 Discovery and activation

On every Func CLI startup, the workload subsystem builds the in-process
list of live workloads in a fixed sequence. The whole flow is one JSON
read plus N assembly loads, where N is the number of **active** versions
in the registry, not the total entry count.

1. **Read the registry.** Open
   `<workload-home>/workloads.json` once. Validate `$schema` (§10.1);
   reject unrecognized values via `GracefulException`. The registry
   carries:
   - `workloads[]` — every installed `(packageId, packageVersion)` row.
   - `activeVersions: { <packageId>: <packageVersion> }` — the pointer
     map maintained by `install` / `update` / `uninstall` (see §6.1
     step 7, §6.4, §6.5).
2. **Select active entries.** For each distinct `packageId` in
   `workloads[]`, pick the version to load:
   - If `activeVersions[<packageId>]` is set, the workload is **pinned**:
     load the named version. If the named version is missing from
     `workloads[]`, surface a `[<packageId>]` warning and skip this
     workload (the user can re-pin via `func workload use` or remove
     the pin).
   - Otherwise the workload is **unpinned**: load the highest semver
     among the rows for that `packageId`.

   Inactive side-by-side rows (older versions of an unpinned workload,
   or non-pinned versions of a pinned workload) are left on disk for
   rollback (§6.4) and surfaced only by
   `func workload list --all-versions`.
3. **Validate compatibility.** If the entry's `workload.json` declared
   `minCliVersion`, compare against the running CLI's semver. If the
   CLI is older, fail this workload's load with
   `[<packageId>] Func CLI too old, please update.` (see §10.2). Other
   workloads continue to load.
4. **Load the assembly.** Compute the assembly path under
   `<workload-home>/workloads/<packageId>/<packageVersion>/<entryPoint.assemblyPath>`
   and reject anything that resolves outside the workloads root
   (defense in depth against a tampered registry). Open it in a fresh
   collectible `AssemblyLoadContext`, one ALC per active workload, so
   conflicting transitive dependencies between workloads cannot clash.
5. **Resolve the entry-point type.** Look up `entryPoint.type` in the
   loaded assembly. The type **must** derive from the abstract
   `Workload` base class (§5); a non-`Workload` type fails this entry's
   load with an `[<packageId>]` error and the install path, suggesting
   `func workload uninstall && install`.
6. **Instantiate.** Call `Activator.CreateInstance` to construct the
   entry-point. The CLI **must not** invoke any other workload methods
   during discovery; the type's static constructor is the only workload
   code that runs, so workloads **must** keep static initialization
   side-effect-free.
7. **Configure.** Once all active workloads are instantiated, the CLI
   walks the resulting list and calls each `Workload.Configure(builder)`
   exactly once, in registry order. This is where contribution-point
   services (`IProjectInitializer`, `IProjectDetector`,
   `ITemplateProvider`, `IPackProvider`, `IExternalCommand`,
   `IHostRuntimeLauncher` — see §5.1) are added to the CLI's DI
   container.
8. **Cache for the process lifetime.** The list is cached in
   `IWorkloadProvider` (singleton). Subsequent commands within the same
   `func` invocation reuse it; they never re-read the registry or
   re-instantiate workloads.

#### Failure isolation

A load failure for one workload (missing assembly, type-not-found,
`minCliVersion` mismatch, etc.) **must not** abort discovery for the
others. The CLI continues with the remaining active workloads and
surfaces the failed one as a non-fatal `[<packageId>]`-prefixed warning
on the next command that would have used it.

#### Multiple-version policy

Side-by-side installs are allowed (`func workload install` does not
remove prior versions; `--prune` on `update` does). Each `packageId`
is in one of two states:

- **Unpinned (default).** No entry in `activeVersions`. The loader
  picks the highest installed semver. New installs and updates are
  picked up automatically on the next `func` invocation.
- **Pinned.** `activeVersions[<packageId>] = <version>`, set by
  `func workload use <package> --version <v>`. The loader honors the
  pin verbatim; new installs and updates do **not** silently move the
  pointer.

Switching versions (or moving between pinned and unpinned) is done via
`func workload use` (§4.1). It is a registry-only operation: no
catalog round-trip, no re-download. The change takes effect on the
next `func` invocation; the running process keeps its cached
workloads.

Per-project version pinning (profiles) is out of scope for v1 and
tracked in §12.

### 6.3 Invocation

- The Func CLI dispatches each request (init/new/pack/etc.) to whichever
  workload(s) registered the corresponding contribution-point service
  (see §5.1). When more than one workload registers the same service,
  the dispatch rules of the consuming command apply (e.g. `--stack`
  disambiguation, project detection routing).
- Errors from a workload **must** surface to the user with the workload id
  prefixed (e.g. `[python] error: ...`).
- The Func CLI **must** distinguish between workload-protocol errors
  (bug, ask user to file an issue) and user-facing errors (display
  verbatim) by **exception type**: a `GracefulException` thrown from a
  workload is a user-facing error and surfaced verbatim; any other
  exception type is treated as a protocol error, prefixed with the
  workload id, and accompanied by a "please file an issue against the
  workload" hint.

### 6.4 Update

- `func workload update <package>` resolves a newer version and installs
  it side-by-side under
  `<workload-home>/workloads/<packageId>/<newVersion>/`.
- If the package is **unpinned** (default), the new row alone is enough:
  the loader's "highest installed semver wins" rule (§6.2 step 2) makes
  the new version live on the next `func` invocation. No registry
  pointer is touched.
- If the package is **pinned**, `update` installs the new row but does
  **not** silently move the pin. It prints a hint: `Run 'func workload
  use <package> --version <new>' to switch.`
- If the new version fails install validation (workload.json missing or
  malformed, entry-point type not found, etc.) the partially staged
  install directory is cleaned up and the registry is not modified.
- Old versions remain on disk (and in `workloads[]`) until `--prune` is
  passed, allowing rollback via `func workload use <package> --version
  <prior>`.

### 6.5 Removal

- `func workload uninstall <package>` removes the active version: it
  deletes the version's install directory and removes its row from
  `workloads[]`. If the package was **pinned** to the removed version,
  the pin is dropped. The package id reverts to unpinned and, if other
  versions remain, the loader's "highest semver wins" rule picks the
  next live version automatically. The user is informed
  ("Active version is now `<package>@<promoted>`.").
- `--version <v>` removes only the named version. If `<v>` is the
  currently active version (whether pinned or unpinned-by-max-semver),
  the same rules apply: drop the pin if pinned to `<v>`; the loader
  promotes the next-highest installed semver on the next invocation.
- `--all-versions` removes every installed version of the package, drops
  any pin, and removes every row.
- Other installed workloads must be unaffected.
- `uninstall` acquires the same registry lock as `install` (see §4.1
  "Required behaviors"). Concurrent `func <command>` invocations either
  complete with the prior version (if they discovered before the lock
  was acquired) or fail to launch the workload after removal.

### 6.6 Background staleness check

- The Func CLI **may** periodically check the catalog for newer versions of
  installed workloads and surface a one-line hint (`A new version of
  python (1.2.3) is available. Run 'func workload update python' to
  upgrade.`).
- Frequency: at most once per 24h. Network failures must be silent.
- Disabled by `FUNC_CLI_Workloads__DisableUpdateCheck=true` (follows
  the configuration-binding env-var convention from §11).

## 7. Error handling requirements

| Scenario                                    | Required behavior                                                                                  |
|---------------------------------------------|----------------------------------------------------------------------------------------------------|
| No workload installed for runtime           | Print exact install command, exit non-zero.                                                       |
| Workload registry unreadable / unknown schema | Throw a `GracefulException` with the registry path and the action to take (update CLI). Do not crash silently. |
| Workload entry-point missing or unloadable  | Print the underlying error and the install path; suggest `func workload uninstall && install`.   |
| Workload fails during request               | Surface the error with `[<workload-id>]` prefix; exit non-zero with the workload's exit code (or 1). |
| Two workloads claim same project / template | Error listing all candidates, suggest `--stack` to disambiguate.                                  |
| Catalog unreachable                         | `install`/`update` must error clearly; `list` and offline operations must still work.             |
| Install failed mid-flight (download / extract / validation) | No partial state on disk; staging directory cleaned up; registry not updated; non-zero exit. |

## 8. Performance requirements

- **CLI startup overhead** for workload discovery (no invocation) must
  remain under **100 ms** for up to 25 installed workloads on a warm cache.
- **First invocation** of a workload (cold) should complete the
  workload-side boot in under **300 ms** for AOT'd workloads and
  **800 ms** for JIT. The host-runtime workload (§4.6) is JIT in v1,
  so plan for the higher figure on first `func start` after install or
  update.
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

### 10.1 Workload registry schema

The workload registry at `~/.azure-functions/workloads.json` carries a
top-level `$schema` field whose value is a versioned JSON Schema URL
(e.g. `https://aka.ms/func/workloads/v1/schema.json`). The current
schema is **v1**.

This follows the same convention used by `tsconfig.json`,
`azure-pipelines.yml`, and `dotnet/global.json`: editors and external
validators can fetch the schema document directly, breaking changes
ship under a new versioned URL (`/v2/`, `/v3/`, ...), and older + newer
CLIs can coexist on disk without one silently corrupting the other.

- The Func CLI **must** check `$schema` on every read.
- A registry whose `$schema` URL the CLI does not recognize **must** be
  rejected with a `GracefulException` instructing the user to update
  the CLI. The CLI **must not** attempt a partial parse.
- A registry with no `$schema` field (legacy, written before the field
  existed) is treated as v1 and re-emitted with the current `$schema`
  URL on the next write.

The v1 registry shape is, at the top level:

```json
{
  "$schema": "https://aka.ms/func/workloads/v1/schema.json",
  "activeVersions": { "<packageId>": "<packageVersion>" },
  "workloads": [ /* WorkloadEntry[] */ ]
}
```

`activeVersions` is **sparse**: it holds entries only for *pinned*
package ids (set via `func workload use --version`, see §4.1).
Unpinned package ids have no entry there; the loader resolves them by
picking the highest installed semver from `workloads[]` (§6.2 step 2).
`workloads[]` is the flat list of every installed
`(packageId, packageVersion)` row, including inactive side-by-side
versions.

### 10.2 Workload compatibility

- Workloads use **semver**; `func workload install` and `update` honour
  the standard NuGet resolution rules.
- A workload **may** declare a `minCliVersion` field in its
  `workload.json` (semver). On load, the CLI compares this against its
  own version and rejects the workload with an `[<workload-id>]`-prefixed
  "Func CLI too old, please update" error if the running CLI is older.
- A workload built against an older abstractions package must continue
  to work as long as the CLI can still satisfy the contract types it
  resolves; otherwise the CLI rejects with "workload too old, please
  run `func workload update`".

## 11. Telemetry expectations

- The Func CLI emits anonymous telemetry per command invocation including:
  `command`, `workload-id` (when one was selected), `outcome`
  (success / user-error / workload-error / cli-error), `duration`.
- Workloads **may** emit their own telemetry but **must not** propagate
  user-identifying data through the Func CLI.
- Telemetry is opt-out via `FUNC_CLI_TELEMETRY_OPTOUT=1`. For
  back-compat with v4, the legacy `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT`
  environment variable is also honored; if both are set, either being
  truthy disables telemetry.

## 12. Open questions

1. **Catalog source** — single NuGet feed by default, or multi-feed with
   precedence rules?
2. **Workload signing** — do we require signed packages for v1, or only
   warn?
3. **Cross-RID workloads** — does `func workload install python` resolve
   per-RID artifacts, or does the workload bundle all RIDs?
4. **Self-contained / single-file `func`** — `WorkloadPaths` already
   supports a configurable root, but a portable single-file `func`
   loses portability the moment a workload is installed unless we
   probe `<func-dir>/workloads` first. Decide before the install
   command lands.
5. **Built-in workload** — does the CLI ship with any workload pre-installed
   (e.g. seed the host-runtime workload on first run), or is the post-install
   state always "no workloads" and `func start` triggers an install prompt?
6. **Host-runtime version pinning** — where does the per-project pin live
   (`host.json`, `local.settings.json`, a new `func.json`, project file)?
7. **Host-runtime workload boundary** — does one workload ship all RIDs +
   all major host versions, or one workload per major (e.g. `...Workload.Host.v4`,
   `...Workload.Host.v5`)?
8. **Profiles / per-project version pinning** — v1 ships a single
   global active version per `packageId` (§6.2 multi-version policy).
   A future profile mechanism (project file, env var, or `func.json`
   sidecar) would let a repo override `activeVersions` for a specific
   working directory. Defer to a later abstractions release; the
   registry schema already isolates the global pointer in
   `activeVersions`, so adding a project-level override is additive.

**Resolved:** *Bidirectional UX* (workload-driven logs, progress,
prompts) is **CLI-rendered only in v1** (see §2 non-goals). A
callback contract for richer UX may be introduced in a later
abstractions release as a non-breaking additive change.
