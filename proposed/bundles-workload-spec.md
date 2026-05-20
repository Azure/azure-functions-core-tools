# Bundles Workload Spec

> **Status:** Draft. This spec defines how Azure Functions **extension
> bundles** are delivered as a content workload in v5 (vnext) Core
> Tools, and how the CLI core resolves the correct version per
> project + active profile. It layers on top of the
> [Workload Spec](workload-spec.md) and the
> [Profile-Based Local Development design](cli-profiles.md). It does
> **not** redefine the bundle on-disk format consumed by the
> Functions runtime host; that contract is unchanged.

## 1. Goals

- Move all bundle acquisition and on-disk layout out of the `func`
  CLI binary and into a `kind: content` workload, mirroring how the
  host runtime ships today (Workload Spec §4.6).
- Keep version selection (host.json ∩ profile intersection,
  highest-installed-wins) inside the CLI core. The bundles workload
  ships **only the bundle payload**; it contains no resolution code
  and registers no DI services.
- Make `func start` resolution **deterministic and fully offline**.
  The CLI performs no network I/O for bundles. All bundle payloads
  are obtained at workload **install** time (the payload ships
  inside the workload `.nupkg`).
- Eliminate Core Tools' bespoke bundle download path
  (`ExtensionBundleHelper`, `DownloadBundleAction`, `func extensions
  install`). The bundles workload is the only acquisition mechanism
  in v5, and acquisition is `func workload install` + nothing else.
- Require no changes in the Functions runtime host. CT keeps using
  the existing `AzureFunctionsJobHost__extensionBundle__downloadPath`
  env var to point the host at the resolved bundle and to suppress
  its built-in probe.

## 2. Non-goals (v1)

- **Project-level bundle pin.** No `--bundle-version` flag, no
  `bundle.version` field in `.func/config.json`. Pinning is done by
  authoring a custom profile (cli-profiles §5.2) that sets
  `extensionBundle.version` to an exact range.
- **Auto-install of the bundles workload on `func start`.** Follows
  the host-workload rule (Workload Spec §4.5): a missing or
  unsatisfying installed workload set causes `func start` to print
  an install hint and exit non-zero. CT never installs workloads
  implicitly.
- **Bundle CDN access from CT at runtime.** Neither the CLI nor the
  bundles workload contacts the bundle CDN at run time. The bundle
  payload is part of the workload `.nupkg`, fetched from the CDN
  exactly once at workload-build time by the workload's own MSBuild
  process.
- **Custom bundles install root.** Bundle payloads live inside the
  workload install directory under `<workload-home>`. There is no
  second on-disk root, no bundles-specific override env var, and no
  plan to add one. (`<workload-home>` itself is customizable via
  `FUNC_CLI_WORKLOADS_HOME`, per Workload Spec §11; this naturally
  relocates bundle installs along with everything else.)
- **Contribution points for bundles.** The bundles workload is
  `kind: content` (Workload Spec §3, §5.3); content workloads
  register no services and contribute no commands. There is **no**
  `IExtensionBundleProvider` interface, no bundles-workload
  assembly load. Resolution is a CLI-core concern.
- **CT → host transport beyond today's env vars.** If a future
  CT-host workload introduces a different startup transport, that
  is out of scope for this spec.

## 3. Definitions

| Term | Meaning |
|------|---------|
| **Bundle id** | The `host.json` `extensionBundle.id` value, e.g. `Microsoft.Azure.Functions.ExtensionBundle` or `Microsoft.Azure.Functions.ExtensionBundle.Preview`. |
| **Bundle version** | A SemVer version of an extension bundle payload, e.g. `4.22.0` (stable) or `4.33.0-experimental` (prerelease). Prerelease tags carry preview / experimental builds. |
| **Bundles workload** | The single `kind: content` workload package id `Azure.Functions.Cli.Workloads.ExtensionBundles`. Each installed instance carries exactly **one** bundle version, packaged at workload-build time. The workload version is **always equal to the bundle version it packages**. |
| **Bundle workload install dir** | The directory the workload subsystem extracts an installed bundles workload into, per Workload Spec §6.1: `<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<bundle-version>/`. |
| **Resolved bundle path** | The directory the CLI core returns to its bundle consumer (today: `func start`). It is a subdirectory of the bundle workload install dir whose contents match today's extension bundle zip layout exactly, so the Functions runtime host needs no changes to consume it. |

## 4. Architecture

### 4.1 Content workload, one bundle version per install

There is **one** workload package id:

```
Azure.Functions.Cli.Workloads.ExtensionBundles
```

It is `kind: content` (Workload Spec §3, §5.3): the package ships
the bundle payload under `tools/any/` with no `entryPoint`. The
workload subsystem records its registry row but does not load it
(Workload Spec §6.2 step 3, §5.1 "non-`workload` kinds"). The CLI
core resolves the install directory by package id and version, in
the same way `func start` resolves the host-runtime workload today
(Workload Spec §4.6).

The workload version equals the bundle version it packages:

| `func workload install …` | Bundle version delivered |
|---------------------------|--------------------------|
| `Azure.Functions.Cli.Workloads.ExtensionBundles@4.22.0` | `4.22.0` |
| `Azure.Functions.Cli.Workloads.ExtensionBundles@4.33.0-preview` | `4.33.0-preview` |
| `Azure.Functions.Cli.Workloads.ExtensionBundles@4.33.0-experimental` | `4.33.0-experimental` |

Multiple bundle versions coexist on disk by installing the workload
multiple times with `func workload install --force` (Workload Spec
§4.6 already specifies this side-by-side model for the host-runtime
workload; bundles use the same mechanism). Each install is a
distinct row in the workload registry.

Rationale:

- A 1:1 mapping between workload version and bundle version makes
  every install reproducible and auditable: the version in
  `workloads.json` is the bundle version on disk, full stop.
- Side-by-side coexistence via `--force` is already part of the
  workload subsystem; bundles don't need a separate cache layer.
- Keeping the workload payload-only (no assembly, no
  contribution points) keeps the per-version `.nupkg` small,
  AOT-friendly for the host CLI, and free of code-signing /
  loading concerns.
- Eliminating a second on-disk root removes an entire class of
  override / migration concerns.

### 4.2 SemVer prerelease tags for preview / experimental

Preview and experimental bundles do **not** get a separate workload
package id. They are expressed as SemVer prerelease versions of the
same package: `4.33.0-preview`, `4.33.0-experimental`. The
prerelease label is a bare channel name with **no** numeric
disambiguator; a republished preview/experimental drop for the same
`<major>.<minor>.<patch>` overwrites its predecessor in the workload
catalog. This matches NuGet's prerelease convention while keeping
the version strings short and obvious.

`host.json` continues to support both bundle ids
(`Microsoft.Azure.Functions.ExtensionBundle` and
`...ExtensionBundle.Preview`). Both ids map to the **same** workload
package; the chosen `extensionBundle.id` plus the requested version
range selects candidates from the installed registry rows. The
exact mapping rules between an `id=...Preview` host.json and which
installed workload versions are considered candidates are captured
as an open question in §9 (the simplest rule is "Preview id selects
only prerelease-tagged versions; stable id selects only
stable-tagged versions").

### 4.3 CLI core ownership

Because the bundles workload is content-only, **all** of the
following live in the `func` CLI core (concretely: in
`Azure.Functions.Cli` against `Func.Cli.Abstractions`):

- Reading the workload registry to enumerate installed bundle
  versions (Workload Spec §10.1).
- Computing the host.json ∩ profile intersection (§5.1).
- Filtering candidates by host.json `extensionBundle.id` (§4.2).
- Picking the highest compatible installed version.
- Resolving the on-disk path (§5.2) and handing it to the host
  via `AzureFunctionsJobHost__extensionBundle__downloadPath`.
- Owning the user-facing log lines, install hints, and telemetry
  events (§6, §5.4, §7).

The bundles workload contains **only** the bundle payload and the
build-time `DownloadFile` plumbing that fetched it from the bundle
CDN (§5.3). It has no runtime code.

#### 4.3.1 Component shape

The CLI-side implementation is factored into three pieces, all
living in CT (not in the workload):

| Component | Layer | Responsibility |
|-----------|-------|----------------|
| `IInstalledBundleWorkloads` | `Func.Cli.Abstractions` | Enumerates installed rows of the bundles content workload from the workload registry. Returns `(version, installDir)` pairs. Implementation wraps the existing workload subsystem (`IWorkloadStore` + `IWorkloadPaths`). |
| `IExtensionBundleResolver` | `Func.Cli.Abstractions` | Runs the algorithm in §5.1. Pure function over `(ProjectContext, IInstalledBundleWorkloads)`; returns an `ExtensionBundleResolution` discriminated union. |
| `ValidateExtensionBundleInitializationStep` | `Azure.Functions.Cli` (`func start` pipeline) | Sole consumer in v1. Calls the resolver, sets the env vars on success, prints the hint and exits non-zero on failure. Also unconditionally sets `AzureFunctionsJobHost__extensionBundle__ensureLatest=false` (see §4.4 step 6). |

`ExtensionBundleResolution` is a closed discriminated union with
four cases matching the outcomes in §5.1: `Resolved`,
`WorkloadMissing`, `EmptyIntersection`, `NoCompatibleInstall`. Each
failure case carries enough structured data for the consumer to
emit the hint copy in §5.4 without re-deriving anything.

The package id constant used by both the CT resolver and the
workload `.csproj` to agree on the bundles package name lives in
`Func.Cli.Abstractions` (e.g. on `IInstalledBundleWorkloads`) so
there is exactly one source of truth.

#### 4.3.2 Why these abstractions live in `Func.Cli.Abstractions`

Even though the bundles workload itself ships no runtime assembly
(content-only), the **resolver** and the **installed-workloads
enumerator** are written against `Func.Cli.Abstractions` rather
than CT's internals. This keeps two doors open:

- A future non-content bundles delivery model could provide its
  own `IInstalledBundleWorkloads` implementation without touching
  CT.
- An alternative consumer (e.g. `func pack` validation) can reuse
  `IExtensionBundleResolver` without depending on `func start`
  pipeline internals.

Neither door is exercised in v1; the abstractions cost is small
and keeps the layering consistent with the rest of the workload
ecosystem.

#### 4.3.3 Folder isolation in CT

All bundles-specific code in `Azure.Functions.Cli` lives under a
single top-level folder (`Bundles/`), and the abstractions in
`Func.Cli.Abstractions` live under a matching `Bundles/`
sub-namespace. Concretely:

```
src/Cli/func/
  Bundles/
    IExtensionBundleResolver.cs       # (abstraction re-exported)
    ExtensionBundleResolver.cs
    InstalledBundleWorkloads.cs       # IInstalledBundleWorkloads impl
    ExtensionBundleResolution.cs      # result union
    BundleHintBuilder.cs              # §5.4 copy
    BundleTelemetry.cs                # §7 event shape
src/Cli/Abstractions/
  Bundles/
    IInstalledBundleWorkloads.cs
    IExtensionBundleResolver.cs
```

The only file outside `Bundles/` that knows about bundles is
`ValidateExtensionBundleInitializationStep` in the `func start`
pipeline, and it talks to the resolver purely through
`IExtensionBundleResolver`. No other CT subsystem references types
under `Bundles/` directly.

This isolation is a soft guarantee that the bundles resolver can
be lifted into a separate assembly (e.g. an
`Azure.Functions.Cli.Bundles` library, or back into a `kind:
workload`-style package) later without touching the rest of CT.

### 4.4 `func start` responsibilities

`func start` is the only v1 consumer. Its responsibilities:

1. Detect whether the project's `host.json` declares an
   `extensionBundle` section. If not, **skip** bundle resolution
   entirely (do not read the workload registry, do not set any env
   var).
2. Resolve the active profile (cli-profiles §6).
3. Run the resolution algorithm (§5.1).
   - On `WorkloadMissing` (no rows for the bundles package at any
     version), print the install hint:
     ```
     This project requires an extension bundle but no bundles
     workload is installed. Install a version with:
       func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@<version>
     ```
     Exit non-zero.
4. On `Resolved`: set
   `AzureFunctionsJobHost__extensionBundle__downloadPath` to the
   returned path and launch the host. Log at Information:
   ```
   Using extension bundle <id> <version> from <path>
   ```
5. On `EmptyIntersection` or `NoCompatibleInstall`: print the
   structured install hint (§5.4) and exit non-zero. Do not start
   the host.
6. **Always** set
   `AzureFunctionsJobHost__extensionBundle__ensureLatest=false`
   when launching the host, whether or not a bundle was resolved.
   In v5 the bundles workload owns the bundle on disk; the host
   runtime must never reach out for a newer bundle behind CT's
   back.

## 5. Resolution and on-disk layout (CLI core)

### 5.1 Resolution algorithm

Given the project context (host.json `extensionBundle` block,
worker runtime, active profile), the CLI core:

1. Computes the **constraint range** as the NuGet-style intersection
   of:
   - the `host.json` `extensionBundle.version` range, and
   - the active profile's `extensionBundle.version` range (if
     declared; otherwise the constraint equals the host.json range).
2. Enumerates **installed bundle workload versions** by reading the
   workload registry (`workloads.json`, Workload Spec §10.1) for
   rows whose `packageId` is
   `azure.functions.cli.workloads.extensionbundles`.
3. Filters that set by host.json `extensionBundle.id` per §4.2
   (stable id → stable-tagged versions; Preview id →
   prerelease-tagged versions; final rule per §9 open question).
4. Selects the **highest** filtered version that satisfies the
   constraint range.
5. Produces one of:
   - **Resolved**: the absolute path inside that workload's install
     dir whose layout matches the extension bundle zip (§5.2).
   - **WorkloadMissing**: no rows for the bundles package id exist
     at any version.
   - **EmptyIntersection**: the host.json range and the profile
     range do not overlap. Carries the highest version that would
     satisfy host.json alone (for the install hint).
   - **NoCompatibleInstall**: the constraint range and a list of
     installed bundle workload versions are non-empty but none
     match. Carries the install hint for a satisfying version.
6. If the active profile declares `supportedRuntimes` and the
   project's worker runtime is not listed, the result includes a
   non-fatal warning. `func start` logs it and proceeds; mismatch
   is informational for bundles and **must not** block start.

The CLI core performs **no** network I/O.

### 5.2 Where bundle content lives

The bundles workload `.nupkg` ships its payload under `tools/any/`,
which the workload subsystem extracts into:

```
<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<bundle-version>/
```

The actual bundle content (the layout the Functions runtime host
already knows how to read: `bin/`, `extensions.json`, etc.) lives
under that directory at a fixed subpath chosen by the workload
package. The CLI core knows that subpath (it is part of the
content-workload contract, documented alongside the package) and
returns the absolute path to it.

No copy or relocation step is performed at install time, and there
is no second on-disk root.

### 5.3 Packaging (workload build time)

The bundles workload `.csproj` is a packaging-only project (no
runtime assembly). It uses the MSBuild `DownloadFile` task to
fetch the bundle payload for the target version from the bundle
CDN once, at workload-build time, and packs it into the `.nupkg`
under `tools/any/`. Practical consequences:

- The CDN is contacted by the **build pipeline that publishes the
  workload**, not by any user machine running `func`.
- A user's machine acquires the bundle through the normal
  `func workload install` flow, which downloads the workload
  `.nupkg` from its catalog feed (Workload Spec §6.1).
- Reproducibility: an installed workload version always carries
  the exact bundle payload that was packed at build time. There
  is no drift between "what the workload version says" and
  "what's on disk."
- Preview / experimental versions are produced the same way; the
  only difference is the SemVer prerelease tag on the workload
  version and the corresponding URL the `DownloadFile` task
  pulls from.

### 5.4 Install hints

The CLI core owns hint copy for the failure variants of §5.1:

- `EmptyIntersection`:
  ```
  Profile '<profile>' constrains extensionBundle.version to
  '<profile-range>', which does not intersect host.json's
  '<host-range>'. There is no bundle version that satisfies both.
  Either widen host.json or pick a different profile.
  ```
- `NoCompatibleInstall`:
  ```
  No installed bundles workload satisfies <constraint-range>
  (id=<bundle-id>).
  Installed versions: <v1>, <v2>, ...
  Install a satisfying version with:
    func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@<suggested-version> --force
  ```
- `WorkloadMissing` hint: see §4.4 step 3.

## 6. Logging and verbosity

- **Default verbosity**: on resolution success, a single
  Information line: `Using extension bundle <id> <version> from
  <path>`. On failure, the hint and a non-zero exit. Nothing else.
- **`--verbose`**: in addition to the above, log the resolution
  trace at Debug:
  ```
  [bundle-resolve] id=<id>
  [bundle-resolve] host.json range = <host-range>
  [bundle-resolve] profile '<profile>' range = <profile-range>
  [bundle-resolve] constraint = <intersection>
  [bundle-resolve] installed versions (filtered) = <v1>, <v2>, ...
  [bundle-resolve] selected = <version>
  [bundle-resolve] path = <resolved-path>
  ```

## 7. Telemetry

On every `func start` that performs bundle resolution, CT emits a
single `bundle-resolve` event:

| Field | Value |
|-------|-------|
| `bundleId` | `host.json` `extensionBundle.id` |
| `resolvedVersion` | Selected bundle version, or `null` on failure |
| `profileName` | Active profile name, or `null` if no profile |
| `succeeded` | `true` if the host was launched with a resolved bundle |
| `reason` | `ok` \| `no-host-json-bundle` \| `workload-missing` \| `empty-intersection` \| `no-compatible-install` |

Workload install / update / uninstall telemetry is covered by the
generic workload subsystem (Workload Spec §11); this spec adds
none there.

## 8. Compatibility & migration

- **v4 → v5 projects.** Existing projects whose `host.json`
  declares `extensionBundle` work unchanged in v5, **provided** an
  installed bundles workload version satisfies the project's
  host.json range and the active profile.
- **`func extensions install`.** Removed in v5. Added to the
  v4→v5 migration map (Workload Spec §4.7):
  ```
  'extensions install' is no longer supported. Extension bundles
  are delivered as a workload. Install a version with:
    func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@<version>
  ```
- **In-CLI bundle download code.** `ExtensionBundleHelper`,
  `DownloadBundleAction`, `ExtensionBundleConfigurationBuilder`,
  and the related code paths in `Startup` are removed from the
  `func` binary in v5.
- **Profiles.** Built-in profiles (cli-profiles §6.1) carry
  `extensionBundle.version` ranges the bundles workload catalog is
  guaranteed to satisfy at publication time.

## 9. Open questions

1. **Bundle id mapping rule.** When host.json declares
   `id=Microsoft.Azure.Functions.ExtensionBundle.Preview`, which
   installed workload versions are candidates? Proposed: only
   versions carrying a prerelease tag whose label is `preview` or
   `experimental` (case-insensitive). Confirm the exact rule and
   whether `.Preview` is still the only "preview-y" id in v5.
2. **Payload subpath inside `tools/any/`.** What's the canonical
   relative path the CLI core appends to the install dir to reach
   the host-consumable bundle layout? Whatever it is, it should be
   documented next to the workload package and treated as part of
   the content-workload contract (Workload Spec §5.1 "the contract
   between the consumer and the content workload is owned by the
   consumer").
3. Should `bundle-resolve` telemetry include constraint range
   strings (potentially high cardinality) or just the outcome
   enum?
4. **Catalog feed.** Do bundle workload `.nupkg`s ship to the same
   workload catalog as host-runtime workloads, or a dedicated
   bundles feed? Most likely the same feed; verify with the
   Extension Bundle publishing pipeline owners.
