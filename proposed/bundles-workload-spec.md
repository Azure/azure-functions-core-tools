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
  implicitly. Auto-install gated on profile opt-in is a separate
  cross-cutting concern (will apply to host runtime, bundles, and
  any other content workload) and is out of scope for this spec.
- **Bundle CDN access from CT at runtime.** Neither the CLI nor the
  bundles workload contacts the bundle CDN at run time. The bundle
  payload is part of the workload `.nupkg`, fetched from the bundle
  build pipeline's artifacts (or, interim, the CDN) exactly once at
  workload-build time by the workload's own MSBuild process. See
  §5.3 for the payload-source pivot.
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
| **Bundle id** | The `host.json` `extensionBundle.id` value. Three ids exist in v5: `Microsoft.Azure.Functions.ExtensionBundle` (stable), `Microsoft.Azure.Functions.ExtensionBundle.Preview`, and `Microsoft.Azure.Functions.ExtensionBundle.Experimental`. Each id is its own channel; experimental is **not** a label on the Preview id. |
| **Bundle payload version** | The 3-part SemVer version of the extension bundle payload zip (e.g. `4.35.0`). This is the version the Functions runtime knows about and the version the user sees in `func` logs. It carries **no** prerelease label, even for the preview / experimental channels. |
| **Workload pkg version** | The version of the bundles workload `.nupkg`. Maps **1:1** to the bundle payload version: stable channel uses the bare 3-part version (e.g. `4.35.0`); preview / experimental channels use the bare 3-part version with a channel prerelease label (e.g. `4.35.0-preview`, `4.35.0-experimental`). See §4.1.1. |
| **Bundles workload** | The single `kind: content` workload package id `Azure.Functions.Cli.Workloads.ExtensionBundles`. Each installed instance carries exactly **one** bundle payload, packaged at workload-build time. |
| **Bundle workload install dir** | The directory the workload subsystem extracts an installed bundles workload into, per Workload Spec §6.1: `<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<workload-pkg-version>/`. |
| **Resolved bundle path** | The directory the CLI core returns to its bundle consumer (today: `func start`). It is a subdirectory of the bundle workload install dir whose contents match today's extension bundle zip layout exactly, so the Functions runtime host needs no changes to consume it. |

## 4. Architecture

### 4.1 Content workload, one bundle payload per install

There is **one** workload package id:

```
Azure.Functions.Cli.Workloads.ExtensionBundles
```

It is `kind: content` (Workload Spec §3, §5.3): the package ships
the bundle payload under `tools/any/<BundleVersion>/` (see §5.2 for
the version-nest contract) with no `entryPoint`. The workload
subsystem records its registry row but does not load it (Workload
Spec §6.2 step 3, §5.1 "non-`workload` kinds"). The CLI core
resolves the install directory by package id and workload pkg
version, in the same way `func start` resolves the host-runtime
workload today (Workload Spec §4.6).

#### 4.1.1 Version scheme

The workload pkg version maps **1:1** to the bundle payload
version. Because the workload pkg is content-only (no workload
code), there is no reason to version it independently of the
payload it carries.

Two MSBuild props in `Directory.Version.props` drive
`<VersionPrefix>`:

- `$(BundleVersion)` (e.g. `4.35.0`): the 3-part SemVer version of
  the bundle payload. Drives the CDN URL (§5.3.2) and the
  user-facing version in `func` logs.
- `$(BundleChannel)` (`stable` | `preview` | `experimental`):
  selects the CDN bundle id at pack time (§5.3.1) and, for
  non-stable channels, the prerelease label appended to the
  workload pkg version.

The resulting workload pkg versions per channel:

| Channel | host.json `extensionBundle.id` | Workload pkg version | Bundle payload version |
|---|---|---|---|
| stable | `Microsoft.Azure.Functions.ExtensionBundle` | `4.35.0` | `4.35.0` |
| preview | `Microsoft.Azure.Functions.ExtensionBundle.Preview` | `4.35.0-preview` | `4.35.0` |
| experimental | `Microsoft.Azure.Functions.ExtensionBundle.Experimental` | `4.35.0-experimental` | `4.35.0` |

Each installed workload pkg version ships **exactly one** bundle
payload version; the 1:1 contract holds at every level.

Rationale:

- Content-only pkg: no workload code to revision separately, so a
  4th-segment iteration counter would be cognitive cost with no
  payoff.
- The prerelease label is NuGet's built-in "this is preview"
  signal: `func workload install` won't pick up preview /
  experimental rows without an explicit pin or `--prerelease`.
- A workload-only republish (build pipeline change, packaging fix,
  etc.) bumps the bundle payload's patch version. If that case
  ever becomes common, we can introduce an iteration knob then.

Multiple bundle versions coexist on disk by installing the workload
multiple times with `func workload install --force` (Workload Spec
§4.6 already specifies this side-by-side model for the host-runtime
workload; bundles use the same mechanism). Each install is a
distinct row in the workload registry, keyed by workload pkg
version.

Other rationale:

- Side-by-side coexistence via `--force` is already part of the
  workload subsystem; bundles don't need a separate cache layer.
- Keeping the workload payload-only (no assembly, no
  contribution points) keeps the per-version `.nupkg` small,
  AOT-friendly for the host CLI, and free of code-signing /
  loading concerns.
- Eliminating a second on-disk root removes an entire class of
  override / migration concerns.

### 4.2 Three-channel id model and resolver filter

`host.json` `extensionBundle.id` selects the **channel**. Each of
the three v5 ids maps to the same workload package
(`Azure.Functions.Cli.Workloads.ExtensionBundles`); the CLI core
resolver picks installed rows by matching the workload pkg
version's prerelease label against the requested id:

| host.json `extensionBundle.id` | Matching workload pkg versions |
|---|---|
| `Microsoft.Azure.Functions.ExtensionBundle` | versions with **no** prerelease label (e.g. `4.35.0`) |
| `Microsoft.Azure.Functions.ExtensionBundle.Preview` | versions whose prerelease label is **exactly `preview`** (e.g. `4.35.0-preview`) |
| `Microsoft.Azure.Functions.ExtensionBundle.Experimental` | versions whose prerelease label is **exactly `experimental`** (e.g. `4.35.0-experimental`) |

The match is exact: a workload version's prerelease label is the
encoded channel. Preview does not match experimental versions and
vice versa.

The version-range intersection (§5.1) is then evaluated against
the **bundle payload version** (3-part `$(BundleVersion)`, which
for stable channel is the workload pkg version itself, and for
non-stable channels is the workload pkg version with the
prerelease label stripped) of each matching candidate.

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
build-time plumbing that fetched it from the bundle build pipeline
(or, interim, the CDN: §5.3). It has no runtime code.

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
    ExtensionBundleResolver.cs
    InstalledBundleWorkloads.cs       # IInstalledBundleWorkloads impl
    ExtensionBundleResolution.cs      # result union
    BundleHintBuilder.cs              # §5.4 copy
    BundleTelemetry.cs                # §7 event shape
    ValidateExtensionBundleInitializationStep.cs  # func start consumer
src/Cli/Abstractions/
  Bundles/
    IInstalledBundleWorkloads.cs
    IExtensionBundleResolver.cs
```

`ValidateExtensionBundleInitializationStep` is registered into the
`func start` initialization pipeline from its usual composition
root, but the type itself lives under `Bundles/` so the whole
feature is colocated. No other CT subsystem references types under
`Bundles/` directly.

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
       func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@<workload-pkg-version>
     ```
     e.g. `...@4.35.0` for stable or `...@4.35.0-preview` for
     preview. Exit non-zero.
4. On `Resolved`: set
   `AzureFunctionsJobHost__extensionBundle__downloadPath` to the
   returned path and launch the host. Log at Information:
   ```
   Using extension bundle <id> <bundle-payload-version> from <path>
   ```
   where `<bundle-payload-version>` is the 3-part bundle version
   (e.g. `4.35.0`), not the workload pkg version.
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
2. Enumerates **installed bundle workload rows** by reading the
   workload registry (`workloads.json`, Workload Spec §10.1) for
   rows whose `packageId` is
   `azure.functions.cli.workloads.extensionbundles`. Each row's
   identity is its workload pkg version (e.g. `4.35.0`,
   `4.35.0-preview`).
3. Filters those rows by host.json `extensionBundle.id` per the
   §4.2 channel rule (stable id → no prerelease label; Preview id
   → prerelease label exactly `preview`; Experimental id →
   prerelease label exactly `experimental`).
4. Projects each surviving row to its **bundle payload version**
   (3-part `$(BundleVersion)`) and selects the **highest** that
   satisfies the constraint range.
5. Produces one of:
   - **Resolved**: the absolute path inside that workload's install
     dir whose layout matches the extension bundle zip (§5.2). The
     resolution carries the **3-part bundle payload version** as
     its user-facing version, not the workload pkg version (the
     two differ only by the prerelease label on non-stable
     channels).
   - **WorkloadMissing**: no rows for the bundles package id exist
     at any version.
   - **EmptyIntersection**: the host.json range and the profile
     range do not overlap. Carries the highest bundle payload
     version that would satisfy host.json alone (for the install
     hint).
   - **NoCompatibleInstall**: the constraint range and a list of
     filtered installed bundle workload rows are non-empty but
     none match. Carries the install hint for a satisfying
     version.
6. If the active profile declares `supportedRuntimes` and the
   project's worker runtime is not listed, the result includes a
   non-fatal warning. `func start` logs it and proceeds; mismatch
   is informational for bundles and **must not** block start.

The CLI core performs **no** network I/O.

### 5.2 On-disk layout

The bundles workload `.nupkg` ships its payload under
`tools/any/<BundleVersion>/`, which the workload subsystem extracts
into:

```
<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<workload-pkg-version>/
  tools/any/
    <bundle-version>/          ← extension bundle root (bin/, extensions.json, ...)
  workload.json
```

The install dir is keyed by the workload pkg version (e.g. `4.35.0`,
`4.35.0-preview`), so stable and preview installs of the same
bundle payload coexist as distinct directories. Inside, the bundle
content (the layout the Functions runtime host already knows how
to read: `bin/`, `extensions.json`, etc.) lives one level deeper
in a directory named after the 3-part bundle payload version.

#### 5.2.1 Resolved path and host probe contract

The CLI core's bundle resolver returns the **parent** of the bundle
version directory: `<install>/tools/any/`. The `func start` step
then sets:

```
AzureFunctionsJobHost__extensionBundle__downloadPath = <install>/tools/any
AzureFunctionsJobHost__extensionBundle__ensureLatest = false
```

The Functions runtime host's `ExtensionBundleManager` probes
`<downloadPath>/<bundle-version>/...` for the bundle root, which
lands on the version directory we just shipped. The version segment
in the layout is what makes the host happy without CT having to set
custom env vars or patch the host probe path.

A practical consequence: a bundles workload `.nupkg` could in
principle ship more than one bundle version under `tools/any/`
(e.g. `tools/any/4.35.0/` and `tools/any/4.36.0/`) and the host
would still probe correctly. Today each workload pkg carries
exactly one bundle version per the 1:1 rule in §4.1.1, but the
layout leaves that door open.

No copy or relocation step is performed at install time, and there
is no second on-disk root.

### 5.3 Packaging (workload build time)

The bundles workload `.csproj` is a packaging-only project (no
runtime assembly). At workload-build time, MSBuild fetches the
bundle payload zip for the target version, unpacks the bundle into
the workload output, and packs the result into the `.nupkg` under
`tools/any/<BundleVersion>/` (see §5.2 for why the version nest is
required).

Other `kind: content` workloads can still pack flat at `tools/any/`
if the consumer doesn't impose a version-probing contract; the
nested layout is specific to bundles because the consumer is the
host's `ExtensionBundleManager`.

#### 5.3.1 Payload source: bundle pipeline build artifacts

The canonical payload source is the **bundle build pipeline's
artifacts feed** in Azure DevOps, not the public bundle CDN.

- The bundles repo publishes a pipeline build per branch. The
  three branches that matter for this spec are:
  - `main` → stable bundle versions.
  - `main-preview` → preview bundle versions.
  - `main-experimental` → experimental bundle versions.
- Each successful build publishes an artifact named `zip` that
  contains, among other things, files of the shape:
  `Microsoft.Azure.Functions.ExtensionBundle.<version>_any-any.zip`
  (one per platform; bundles ship a platform-neutral `any-any`
  variant which is the one this workload uses).
- The bundles-workload build pipeline downloads exactly that file
  for the target `<version>` from the build matching the SemVer
  prerelease channel (stable / preview / experimental).

This gives the bundles workload the same payload that has been
signed and validated by the bundles release process, with no
dependency on the public CDN at workload-build time and no
dependency on the CDN at user-run time.

#### 5.3.2 Interim: temporary CDN fetch

For the first cut, while the workload still lives in
`Azure/azure-functions-core-tools` and is decoupled from the
bundles release schedule, MSBuild uses `DownloadFile` against the
**public bundle CDN** as a quick-setup payload source. This is an
implementation detail of the workload build and has no effect on
the on-disk shape or on `func` runtime behavior.

The pivot to build artifacts happens when:

- The bundles workload moves into the bundles repo (or is wired
  into the bundles release pipeline from this repo), so the
  workload `.nupkg` and the bundle `.zip` are produced by the same
  build and the artifact handle is naturally available; or
- The bundle release process exposes a stable cross-pipeline
  artifact download URL the workload build can target without
  manual handling.

Until then, refreshing the pinned bundle payload requires manually
downloading the artifact zip from the bundle pipeline run and
updating the `DownloadFile` URL + checksum in the workload csproj.
Tracking this transition is open question §9.Q5.

#### 5.3.3 Consequences (shared between both payload sources)

- The payload source is contacted by the **build pipeline that
  publishes the workload**, not by any user machine running
  `func`.
- A user's machine acquires the bundle through the normal
  `func workload install` flow, which downloads the workload
  `.nupkg` from its catalog feed (Workload Spec §6.1).
- Reproducibility: an installed workload version always carries
  the exact bundle payload that was packed at build time. There
  is no drift between "what the workload version says" and "what's
  on disk."
- Preview / experimental versions are produced the same way; the
  only difference is the SemVer prerelease tag on the workload
  version and the corresponding bundle artifact branch
  (`main-preview` / `main-experimental`) the build pulls from.

### 5.4 Install hints

The CLI core owns hint copy for the failure variants of §5.1.
Suggested pin versions in hints use the **workload pkg version**
(with channel prerelease label as applicable):

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
    func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@<workload-pkg-version> --force
  ```
  e.g. `...@4.35.0` for stable, `...@4.35.0-preview` for
  preview.
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
  [bundle-resolve] installed workload pkg versions (filtered) = <wpv1>, <wpv2>, ...
  [bundle-resolve] installed bundle payload versions = <bpv1>, <bpv2>, ...
  [bundle-resolve] selected bundle payload version = <bundle-payload-version>
  [bundle-resolve] path = <resolved-path>
  ```

## 7. Telemetry

On every `func start` that performs bundle resolution, CT emits a
single `bundle-resolve` event:

| Field | Value |
|-------|-------|
| `bundleId` | `host.json` `extensionBundle.id` |
| `resolvedVersion` | Selected 3-part **bundle payload version** (e.g. `4.35.0`) on success, or `null` on failure. The workload pkg version (which adds only a channel prerelease label on non-stable channels) is CLI-internal and is not emitted. |
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

1. Should `bundle-resolve` telemetry include constraint range
   strings (potentially high cardinality) or just the outcome
   enum?
2. **Catalog feed.** Do bundle workload `.nupkg`s ship to the same
   workload catalog as host-runtime workloads, or a dedicated
   bundles feed? Most likely the same feed; verify with the
   Extension Bundle publishing pipeline owners.
3. **Payload-source pivot (CDN → build artifacts).** The first
   cut pulls the bundle payload from the public bundle CDN via
   MSBuild `DownloadFile` (§5.3.2) because the bundles workload
   lives in this repo and not in the bundles repo. The target
   state is to consume the bundle build pipeline's `zip` artifact
   per branch (`main` / `main-preview` / `main-experimental`,
   §5.3.1) so the workload version always packs a payload that
   was produced and signed by the same pipeline run. Decide:
   does the bundles workload move into the bundles repo, or
   stay here and consume the artifact cross-pipeline? Either
   option resolves this question. Until it is resolved, refreshing
   the pinned bundle payload is a manual artifact download.

### Resolved

- **Bundle id mapping rule** (previously Q1). Resolved by §4.2:
  three host.json ids (stable / Preview / Experimental), each
  mapping to workload pkg versions whose prerelease label exactly
  matches the channel. Implemented in PR #5036.
- **Payload subpath inside `tools/any/`** (previously Q2).
  Resolved by §5.2: the bundle payload lives under
  `tools/any/<bundle-version>/`, and the CLI core returns the
  parent (`tools/any/`) so the host's `ExtensionBundleManager`
  can append the version segment in its probe. Implemented in
  PR #5055 (packaging) and PR #5036 (resolver
  `BundlePayloadSubpath = "tools/any"`).
