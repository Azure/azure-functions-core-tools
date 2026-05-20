# Bundles Workload Spec

> **Status:** Draft. This spec defines how Azure Functions **extension
> bundles** are resolved and delivered in v5 (vnext) Core Tools. It
> layers on top of the [Workload Spec](workload-spec.md) and the
> [Profile-Based Local Development design](cli-profiles.md). It does
> **not** redefine the bundle on-disk format consumed by the Functions
> runtime host; that contract is unchanged.

## 1. Goals

- Move all bundle acquisition, version selection, and on-disk
  layout out of the `func` CLI binary and into a workload, so bundles
  can ship and version independently of Core Tools.
- Expose a stable contribution point (`IExtensionBundleProvider`)
  that any CLI command (today: `func start`) can call to obtain a
  resolved bundle path for a project + active profile.
- Make `func start` resolution **deterministic and fully offline**.
  The bundles workload performs no network I/O. All bundle payloads
  are obtained at workload **install** time (the payload ships
  inside the workload `.nupkg`).
- Eliminate Core Tools' bespoke bundle download path
  (`ExtensionBundleHelper`, `DownloadBundleAction`, `func extensions
  install`). The bundles workload is the only acquisition mechanism
  in v5, and acquisition is `func workload install` + nothing else.
- Require no changes in the Functions runtime host. CT keeps using the
  existing `AzureFunctionsJobHost__extensionBundle__downloadPath` env
  var to point the host at the resolved bundle and to suppress its
  built-in probe.

## 2. Non-goals (v1)

- **Project-level bundle pin.** No `--bundle-version` flag, no
  `bundle.version` field in `.func/config.json`. Pinning is done by
  authoring a custom profile (`cli-profiles.md` §5.2) that sets
  `extensionBundle.version` to an exact range.
- **Auto-install of the bundles workload on `func start`.** Follows
  the host-workload rule (Workload Spec §4.5): a missing or
  unsatisfying installed workload set causes `func start` to print
  an install hint and exit non-zero. CT never installs workloads
  implicitly.
- **Bundle CDN access from the bundles workload at runtime.** The
  workload never downloads bundles. The bundle payload is part of
  the workload `.nupkg`, fetched from the CDN exactly once at
  workload-build time by the workload's own MSBuild process.
- **Custom bundles install root.** Bundle payloads live inside the
  workload install directory under `<workload-home>`. There is no
  second on-disk root, no override env var, and no plan to add one.
- **CT → host transport beyond today's env vars.** If a future
  CT-host workload introduces a different startup transport, that is
  out of scope for this spec.

## 3. Definitions

| Term | Meaning |
|------|---------|
| **Bundle id** | The `host.json` `extensionBundle.id` value, e.g. `Microsoft.Azure.Functions.ExtensionBundle` or `Microsoft.Azure.Functions.ExtensionBundle.Preview`. |
| **Bundle version** | A SemVer version of an extension bundle payload, e.g. `4.22.0` (stable) or `4.33.0-experimental` (prerelease). Prerelease tags carry preview / experimental builds. |
| **Bundles workload** | The single workload package id `Azure.Functions.Cli.Workloads.ExtensionBundles` (`kind: workload`). Each installed instance of this workload carries exactly **one** bundle version, packaged at workload-build time. The workload version is **always equal to the bundle version it packages**. |
| **Bundle workload install dir** | The directory the workload subsystem extracts an installed bundles workload into, per Workload Spec §6.1: `<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<bundle-version>/`. |
| **Resolved bundle path** | The directory the provider returns to a consumer. It is a subdirectory of the bundle workload install dir whose contents match today's extension bundle zip layout exactly, so the Functions runtime host needs no changes to consume it. |

## 4. Architecture

### 4.1 One workload package, one bundle version per install

There is **one** workload package id:

```
Azure.Functions.Cli.Workloads.ExtensionBundles
```

The workload version equals the bundle version it packages:

| `func workload install …` | Bundle version delivered |
|---------------------------|--------------------------|
| `Azure.Functions.Cli.Workloads.ExtensionBundles@4.22.0` | `4.22.0` |
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

### 4.3 `IExtensionBundleProvider` contribution point

Core Tools adds a new contribution point to `Func.Cli.Abstractions`
(Workload Spec §5.1):

| Service registered | The workload can... | Used by |
|--------------------|---------------------|---------|
| `IExtensionBundleProvider` | Resolve a bundle path for a project + active profile from the set of installed bundles workloads. | `func start` |

The contract returns a resolved bundle path (or a structured
failure with enough information for CT to print an actionable
install hint). The bundles workload is the v1 implementer; the
abstraction is open to alternative implementations.

Exact interface signature lives in
[`building-a-workload.md`](./building-a-workload.md). Out of scope
for this spec.

### 4.4 Consumer responsibilities (`func start`)

`func start` is the only v1 consumer. Its responsibilities are
intentionally minimal:

1. Detect whether the project's `host.json` declares an
   `extensionBundle` section. If not, **skip** resolution entirely
   (do not load the bundles workload, do not set any env var).
2. Resolve the active profile (cli-profiles §6).
3. Look up an `IExtensionBundleProvider` from the loaded workloads.
   - If none is registered (i.e. **no** bundles workload installed
     at any version), print the install hint:
     ```
     This project requires an extension bundle but no bundles
     workload is installed. Install a version with:
       func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@<version>
     ```
     Exit non-zero.
4. Invoke the provider, passing the project context (parsed
   `host.json` `extensionBundle` block, worker runtime, active
   profile).
5. On success: set
   `AzureFunctionsJobHost__extensionBundle__downloadPath` to the
   returned path and launch the host. Log at Information:
   ```
   Using extension bundle <id> <version> from <path>
   ```
6. On failure: surface the provider's structured error message
   (which includes the install hint, see §5.3) and exit non-zero.
   Do not start the host.
7. **Always** set `AzureFunctionsJobHost__extensionBundle__ensureLatest=false`
   when launching the host, whether or not a bundle was resolved.
   In v5 the bundles workload owns the bundle on disk; the host
   runtime must never reach out for a newer bundle behind CT's
   back.

`func start` does **not** know about workload install paths,
per-version layout, or the bundle CDN. That is entirely the
workload's concern.

## 5. Bundles workload behavior

### 5.1 Resolution algorithm

Given the consumer-supplied context (host.json `extensionBundle`
block, worker runtime, active profile), the provider:

1. Computes the **constraint range** as the NuGet-style intersection
   of:
   - the `host.json` `extensionBundle.version` range, and
   - the active profile's `extensionBundle.version` range (if
     declared; otherwise the constraint equals the host.json range).
2. Enumerates **installed bundle workload versions** by reading the
   workload registry (`workloads.json`, Workload Spec §6.1) for
   rows whose `packageId` is `azure.functions.cli.workloads.extensionbundles`.
3. Filters that set by host.json `extensionBundle.id` per §4.2
   (stable id → stable-tagged versions; Preview id → prerelease-
   tagged versions; final rule per §9 open question).
4. Selects the **highest** filtered version that satisfies the
   constraint range.
5. Returns either:
   - **Resolved**: the absolute path inside that workload's install
     dir whose layout matches the extension bundle zip (§5.2), or
   - **EmptyIntersection**: the host.json range, the profile range,
     and the highest version that would satisfy host.json alone
     (for the install hint), or
   - **NoCompatibleInstall**: the constraint range and a list of
     installed bundle workload versions, plus the install hint for
     a satisfying version.
6. If the active profile declares `supportedRuntimes` and the
   project's worker runtime is not listed, the provider includes a
   non-fatal warning in its successful result. `func start` logs it
   and proceeds; mismatch is informational for bundles and **must
   not** block start.

The provider performs **no** network I/O.

### 5.2 Where bundle content lives

A bundles workload install puts the bundle payload inside its
standard workload install dir (Workload Spec §6.1 step 6):

```
<workload-home>/workloads/azure.functions.cli.workloads.extensionbundles/<bundle-version>/
```

The actual bundle content (the layout the Functions runtime host
already knows how to read: `bin/`, `extensions.json`, etc.) lives
under that directory in a fixed subpath chosen by the workload
package. The provider returns the absolute path to that subpath; CT
treats it as opaque.

No copy or relocation step is performed at install time, and there
is no second on-disk root.

### 5.3 Packaging (workload build time)

The bundles workload `.csproj` uses the MSBuild `DownloadFile` task
to fetch the bundle payload for the target version from the bundle
CDN once, at workload-build time, and packs it into the workload
`.nupkg`. Practical consequences:

- The CDN is contacted by the **build pipeline that publishes the
  workload**, not by any user machine running `func`.
- A user's machine acquires the bundle through the normal
  `func workload install` flow, which downloads the workload
  `.nupkg` from its catalog feed (Workload Spec §6.1).
- Reproducibility: an installed workload version always carries the
  exact bundle payload that was packed at build time. There is no
  drift between "what the workload version says" and "what's on
  disk."
- Preview / experimental versions are produced the same way; the
  only difference is the SemVer prerelease tag on the workload
  version and the corresponding URL the `DownloadFile` task pulls
  from.

### 5.4 Install hints

When the provider returns `EmptyIntersection` or
`NoCompatibleInstall`, it includes a fully-formed hint string the
consumer prints verbatim. Example hints:

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

The bundles workload owns the wording; CT does not duplicate this
copy.

## 6. Logging and verbosity

- **Default verbosity**: on resolution success, a single Information
  line: `Using extension bundle <id> <version> from <path>`. On
  failure, the provider's hint and a non-zero exit. Nothing else.
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
- The Debug trace is produced by the bundles workload (it has the
  data) and emitted through CT's standard logger; CT does not
  re-derive any of it.

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
generic workload subsystem (Workload Spec §11); this spec adds none
there.

## 8. Compatibility & migration

- **v4 → v5 projects.** Existing projects whose `host.json` declares
  `extensionBundle` work unchanged in v5, **provided** an installed
  bundles workload version satisfies the project's host.json range
  and the active profile.
- **`func extensions install`.** Removed in v5. Added to the v4→v5
  migration map (Workload Spec §4.7):
  ```
  'extensions install' is no longer supported. Extension bundles are
  delivered as a workload. Install a version with:
    func workload install Azure.Functions.Cli.Workloads.ExtensionBundles@<version>
  ```
- **In-CLI bundle download code.** `ExtensionBundleHelper`,
  `DownloadBundleAction`, `ExtensionBundleConfigurationBuilder`, and
  the related code paths in `Startup` are removed from the `func`
  binary in v5.
- **Profiles.** Built-in profiles (cli-profiles §6.1) carry
  `extensionBundle.version` ranges the bundles workload catalog is
  guaranteed to satisfy at publication time.

## 9. Open questions

1. Does the bundles workload expose a `func bundles ...` subcommand
   (via `IExternalCommand`) for ergonomic install / list of
   bundles, or is the bare `func workload install` sufficient?
2. **Bundle id mapping rule.** When host.json declares
   `id=Microsoft.Azure.Functions.ExtensionBundle.Preview`, which
   installed workload versions are candidates? Proposed: only
   versions carrying a prerelease tag whose label set intersects
   `{preview, experimental}` (or similar). Confirm the exact rule
   and whether `.Preview` is still the only "preview-y" id in v5.
3. Should `bundle-resolve` telemetry include constraint range
   strings (potentially high cardinality) or just the outcome enum?
4. Catalog feed: do bundle workload `.nupkg`s ship to the same
   workload catalog as host-runtime workloads, or a dedicated
   bundles feed? (Most likely the same feed; verify with the
   Extension Bundle publishing pipeline owners.)
