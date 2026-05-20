# Bundles Workload Spec

> **Status:** Draft. This spec defines how Azure Functions **extension
> bundles** are resolved and delivered in v5 (vnext) Core Tools. It
> layers on top of the [Workload Spec](workload-spec.md) and the
> [Profile-Based Local Development design](cli-profiles.md). It does
> **not** redefine the bundle on-disk format consumed by the Functions
> runtime host; that contract is unchanged.

## 1. Goals

- Move all bundle acquisition, version selection, and on-disk
  layout out of the `func` CLI binary and into a single first-class
  workload, so bundles can ship and version independently of Core
  Tools.
- Expose a stable contribution point that any CLI command (today:
  `func start`) can call to obtain a resolved bundle path for a
  project + active profile.
- Make `func start` resolution deterministic. When a satisfying
  bundle payload is already cached locally, `func start` resolves
  fully offline. When it isn't, the bundles workload may fetch it
  on demand at start time (see §5.3).
- Eliminate Core Tools' bespoke bundle download path
  (`ExtensionBundleHelper`, `DownloadBundleAction`, `func extensions
  install`). The bundles workload is the only acquisition mechanism
  in v5.
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
  the host-workload rule (Workload Spec §4.5): a missing workload
  causes `func start` to print an install hint and exit non-zero. CT
  never installs workloads implicitly.
- **CT → host transport beyond today's env vars.** If a future
  CT-host workload introduces a different startup transport, that is
  out of scope for this spec.

## 3. Definitions

| Term | Meaning |
|------|---------|
| **Bundle id** | The `host.json` `extensionBundle.id` value, e.g. `Microsoft.Azure.Functions.ExtensionBundle` or `Microsoft.Azure.Functions.ExtensionBundle.Preview`. |
| **Bundle version** | A SemVer version of an extension bundle payload, e.g. `4.22.0`. |
| **Bundles workload** | The single workload package `Azure.Functions.Cli.Workloads.ExtensionBundles` (`kind: workload`) that provides bundle resolution and owns the on-disk payload cache. |
| **Bundles home** | The on-disk root the bundles workload uses for payloads. Defaults to `<workload-home>/bundles` (i.e. `~/.azure-functions/bundles`). Override via `FUNC_CLI_BUNDLES_HOME`. |
| **Resolved bundle path** | A directory path returned by the bundles workload to a consumer (e.g. `func start`). Its layout matches today's bundle zip layout exactly so the Functions runtime host needs no changes to consume it. |

## 4. Architecture

### 4.1 Single workload, not per-version packages

There is **one** workload package:

```
Azure.Functions.Cli.Workloads.ExtensionBundles
```

Its package id does **not** encode a bundle version. Multiple bundle
versions and both the stable and preview bundle ids are handled
inside the workload's payload cache (§5), not as separate workload
registry rows.

Rationale:

- The set of bundle versions a developer might need is open-ended; one
  workload registry row per bundle version would balloon the workload
  manifest and complicate version selection across the profile +
  host.json constraint.
- Bundle payloads have a stable shape the Functions runtime host
  already understands. There is no per-version CLI-side code; the
  only CLI-side code is the resolver. One workload assembly with a
  payload cache is the simplest viable factoring.

### 4.2 `IExtensionBundleProvider` contribution point

Core Tools adds a new contribution point to `Func.Cli.Abstractions`
(Workload Spec §5.1):

| Service registered | The workload can... | Used by |
|--------------------|---------------------|---------|
| `IExtensionBundleProvider` | Resolve a bundle path for a project + active profile, and manage the on-disk bundle payload cache. | `func start` |

The contract returns a resolved bundle path (or a structured failure
with enough information for CT to print an actionable install/update
hint). The bundles workload is the v1 implementer; the abstraction
is open to alternative implementations.

Exact interface signature lives in
[`building-a-workload.md`](./building-a-workload.md). Out of scope
for this spec.

### 4.3 Consumer responsibilities (`func start`)

`func start` is the only v1 consumer. Its responsibilities are
intentionally minimal:

1. Detect whether the project's `host.json` declares an
   `extensionBundle` section. If not, **skip** resolution entirely
   (do not load the bundles workload, do not set any env var).
2. Resolve the active profile (cli-profiles `§6`).
3. Look up an `IExtensionBundleProvider` from the loaded workloads.
   - If none is registered, print the install hint:
     ```
     This project requires an extension bundle but no bundles
     workload is installed. Install it with:
       func workload install Azure.Functions.Cli.Workloads.ExtensionBundles
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
   (which includes the install/update hint, see §5.4) and exit
   non-zero. Do not start the host.
7. **Always** set `AzureFunctionsJobHost__extensionBundle__ensureLatest=false`
   when launching the host, whether or not a bundle was resolved.
   In v5 the bundles workload owns freshness; the host runtime
   must never reach out for a newer bundle behind CT's back.

`func start` does **not** know about the bundles home, the bundle
CDN, or per-version payload layout. That is entirely the workload's
concern.

## 5. Bundles workload behavior

### 5.1 Resolution algorithm

Given the consumer-supplied context (host.json `extensionBundle`
block, worker runtime, active profile), the provider:

1. Computes the **constraint range** as the NuGet-style intersection of:
   - the `host.json` `extensionBundle.version` range, and
   - the active profile's `extensionBundle.version` range (if
     declared; otherwise the constraint equals the host.json range).
2. Enumerates installed bundle payloads under the bundles home whose
   bundle id matches `host.json` `extensionBundle.id`.
3. Selects the **highest installed version** that satisfies the
   constraint range.
4. If no installed version satisfies the constraint, the provider
   **may** fetch a satisfying version from the bundle CDN on demand
   (see §5.3). If the fetch succeeds, the new payload is added to
   the cache and selected.
5. Returns either:
   - **Resolved**: the absolute path
     `<bundles-home>/<bundle-id>/<version>/`, or
   - **EmptyIntersection**: the host.json range, the profile range,
     and the highest version that would satisfy host.json alone (for
     the install hint), or
   - **NoCompatiblePayload**: the constraint range, a list of
     installed versions for the same bundle id, and (if the on-demand
     fetch was attempted and failed) the failure reason.
6. If the active profile declares `supportedRuntimes` and the
   project's worker runtime is not listed, the provider includes a
   non-fatal warning in its successful result. `func start` logs it
   and proceeds; mismatch is informational for bundles and **must
   not** block start.

### 5.2 On-disk layout (bundles home)

The bundles workload manages payloads under:

```
<bundles-home>/<bundle-id>/<bundle-version>/
```

Default `<bundles-home>` is `<workload-home>/bundles`, i.e.
`~/.azure-functions/bundles`. Override via the
`FUNC_CLI_BUNDLES_HOME` environment variable (same convention as
`FUNC_CLI_WORKLOADS_HOME`, Workload Spec §11). Resolution mirrors
`WorkloadHomeResolver` exactly: explicit env var wins, otherwise
nest under the workload home (which itself honors
`FUNC_CLI_WORKLOADS_HOME`).

The contents under `<bundle-version>/` mirror today's extension
bundle zip layout exactly. Returning this directory to a consumer
gives the Functions runtime host a directory it already knows how to
read with no code changes.

### 5.3 Payload acquisition

The bundles workload acquires bundle payloads from one of three
sources:

1. **In-package, at workload build time.** The bundles workload's
   own `.csproj` uses the MSBuild `DownloadFile` task to fetch a
   curated set of **stable** bundle payloads from the bundle CDN at
   workload-build time and packs them into the workload `.nupkg`
   under e.g. `tools/any/bundles/<bundle-id>/<bundle-version>/`.
   On workload install, these payloads are copied (or hard-linked)
   into the bundles home. This is the **preferred** path for stable
   bundles because it gives every fresh workload install a usable
   offline-resolvable payload set with no post-install hook required
   and no first-run network hit. The trade-off is workload package
   size; the curated set should be small (e.g. the latest stable
   `Microsoft.Azure.Functions.ExtensionBundle` major + minor).
2. **Eagerly, via a post-install / first-run hook.** The Workload
   Spec does not define such a hook today (see Workload Spec §12 #5,
   an open question). If/when one is added, the bundles workload may
   use it to grow the default payload set without rebuilding the
   workload itself.
3. **On demand at `func start`.** Used whenever a project requires a
   version that isn't already in the bundles home (e.g. a
   profile-pinned older version, a brand-new stable version
   published after the workload was packaged, or any **preview /
   experimental** bundle id such as
   `Microsoft.Azure.Functions.ExtensionBundle.Preview`). Preview /
   experimental payloads are explicitly **not** shipped inside the
   workload package so that:
   - workload package size doesn't churn with every preview drop,
     and
   - opting into a preview bundle is always an explicit network
     event with a log line, never a silent "you already had it
     locally" because the workload happened to ship it.

In other words: the workload package eagerly carries **stable**
bundle payloads it was built against; **preview / experimental**
bundles are CDN-on-demand only.

On-demand fetch at `func start`:

- The provider **must** print an Information log line before
  initiating download, e.g. `Downloading extension bundle <id>
  <version>…`, and a completion line on success.
- The provider **must** apply a bounded timeout (workload-defined,
  not specified here) and fail fast with a clear hint if the network
  is unavailable, the version cannot be located, or integrity
  verification fails.
- A successful on-demand fetch is observationally identical to a
  pre-cached resolution from `func start`'s perspective: same return
  contract, same env var handling. Telemetry distinguishes the two
  via the `reason` field (§7).

Whether the workload also surfaces a `func bundles install <version>`
subcommand (via `IExternalCommand`, Workload Spec §5.1) for ad-hoc
prefetching is **deferred** (see §9 open questions).

The bundles workload is responsible for verifying download integrity
(checksum/signature) and for atomic extraction under `<bundles-home>`
in both acquisition paths.

### 5.4 Install / update hints

When the provider returns `EmptyIntersection` or
`NoCompatiblePayload`, it includes a fully-formed hint string the
consumer prints verbatim. Example hints:

- `EmptyIntersection`:
  ```
  Profile '<profile>' constrains extensionBundle.version to
  '<profile-range>', which does not intersect host.json's
  '<host-range>'. There is no bundle version that satisfies both.
  Either widen host.json or pick a different profile.
  ```
- `NoCompatiblePayload` (after on-demand fetch was attempted and
  failed, or was not possible offline):
  ```
  No <id> bundle satisfies <constraint-range>.
  Installed versions: <v1>, <v2>, ...
  On-demand download failed: <reason>.
  Try again when online, or update bundles with:
    func workload update Azure.Functions.Cli.Workloads.ExtensionBundles
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
  [bundle-resolve] installed versions = <v1>, <v2>, ...
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
| `reason` | `ok-in-package` \| `ok-cached` \| `ok-downloaded` \| `no-host-json-bundle` \| `workload-missing` \| `empty-intersection` \| `download-failed` |

Workload install / update / uninstall telemetry is covered by the
generic workload subsystem (Workload Spec §11); this spec adds none
there.

## 8. Compatibility & migration

- **v4 → v5 projects.** Existing projects whose `host.json` declares
  `extensionBundle` work unchanged in v5, **provided** the bundles
  workload is installed and has a payload satisfying the project's
  host.json range and the active profile.
- **`func extensions install`.** Removed in v5. Added to the v4→v5
  migration map (Workload Spec §4.7):
  ```
  'extensions install' is no longer supported. Extension bundles are
  delivered as a workload. Install it with:
    func workload install Azure.Functions.Cli.Workloads.ExtensionBundles
  ```
- **In-CLI bundle download code.** `ExtensionBundleHelper`,
  `DownloadBundleAction`, `ExtensionBundleConfigurationBuilder`, and
  the related code paths in `Startup` are removed from the `func`
  binary in v5.
- **Profiles.** Built-in profiles (cli-profiles §6.1) carry
  `extensionBundle.version` ranges the bundle CDN is guaranteed to
  satisfy at publication time. The bundles workload's default payload
  set is curated so that the workload-installed-by-default scenario
  works for those ranges out of the box.

## 9. Open questions

1. Does the bundles workload expose a `func bundles ...` subcommand
   (via `IExternalCommand`) for ad-hoc per-version pulls
   (`func bundles install <version>`), so users can prefetch
   bundles for offline scenarios without waiting for the on-demand
   path at `func start`?
2. **In-package curated set policy.** Which stable bundle versions
   ship inside the workload `.nupkg` via `DownloadFile` at workload
   build time? Latest stable major.minor only, or latest two minors,
   or every minor in a supported range? This trades workload package
   size against how often a fresh install needs an on-demand fetch.
3. Should the workload retain old payloads on update, or prune them?
   If prune, what's the retention policy (last N versions, anything
   referenced by an installed profile, etc.)?
4. Should `bundle-resolve` telemetry include constraint range strings
   (potentially high cardinality) or just an outcome enum?
5. Do we want an explicit "offline mode" flag/env var on `func start`
   that suppresses the on-demand fetch even when network is
   available (for reproducible CI builds, etc.)?
