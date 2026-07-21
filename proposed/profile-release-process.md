# Profile Release Process (Draft)

**Status:** Draft — revised after initial design meeting (2026-07-15)

## 1. Overview

Profiles (`flex`, `windows-consumption`, `windows-dedicated`, `linux-dedicated`,
etc.) are a **constraint set** that pins the host version range, extension bundle
range, worker versions, and feature toggles a SKU supports (see
`cli-profiles.md`). Those constraints must track what is actually deployed to
each SKU in the cloud, and the corresponding workloads must be available for
local install before a profile advertises them.

This doc captures the end-to-end **profile release flow**: from host build
through SKU deployment to profile publication. It builds on
`vnext-release-process.md` (tag + pipeline mechanics) and `cli-profiles.md`
(profile schema and CLI resolution).

> Open questions are called out inline as **Q:** and consolidated in
> [§8. Open questions](#8-open-questions).

## 2. Background: how the host rolls out

- **The entire rollout is production.** There is no internal "staging" channel in
  the host rollout. "Staging vs. public" is a *CLI-profile* construct we layer
  on top.
- **Each SKU has a dedicated pipeline** tied to a specific git tag. The
  pipeline's pool config (or equivalent) is the source of truth for the exact
  versions deployed per SKU. Windows Dedicated and Linux Dedicated are different
  SKUs with separate pipelines.
- **Pool config is the source of truth for Linux/Flex.** A pool config (JSON/XML)
  lists each language (Python, .NET, Java, Node) and its image version. Each
  Linux SKU (Flex Consumption, Linux Dedicated, CV1) has its own pool config and
  EV2 pipeline that pushes it to Cosmos DB per deployment stage.
- **Stack-level rollbacks are done within the pool config.** If a specific worker
  (e.g. Python) has issues, the pool config is updated with N−1 for that stack
  while N is kept for the rest.

## 3. Profile channels

Each SKU has **three profile channels**:

| Channel | Description | Updated when | Scope |
| --- | --- | --- | --- |
| **Default (public)** | Versions matching public cloud deployment. Most customers target this. | End of each SKU completing public cloud release. | **GA** |
| **Slow** | Lags behind default. Named with `-slow` suffix (e.g. `flex-slow`). | TBD | Post-GA — needs design |
| **N−1** | Matches App Service N−1 release channel (previous host/bundle version). | When the default advances (previous default becomes N−1). | Post-GA — needs design |

For GA, only the default channel is required. The slow and N−1 channels are not
necessarily hooked into the automated host release pipeline and require further
design with the respective release owners.

Each channel has its own **registry** — a single `registry.json` file on CDN
containing all SKU profiles for that channel (see [§5](#5-cdn-layout-and-cli-resolution)).

- **Q:** Which SKUs support App Service N−1 channels today? Has this been
  extended to Flex?

## 4. Release flow

```
host.official build ──► create-host-package (Site Extension zip)
        │
        ├─► cut workloads (host + workers) ──► publish to internal/staging NuGet feed
        │                                       └─► upload staging profile to staging CDN
        │
        ├─► Windows: RU (Host + Extension Bundles) ──► EV2 staged rollout
        │
        └─► Linux: EV2 pipeline (pool config per SKU)
              │
              ▼
        per-SKU deployment (each SKU completes independently)
              │
              ▼
        hook at end of each SKU completing release:
              1. workload-promotion pipeline
              2. profile-update pipeline
```

### 4.1 Workload build and staging

When the host is cut:

- All workloads (host + workers) are built and published to the **internal /
  staging NuGet feed** via the existing per-component release pipelines
  (`eng/ci/release/official-release.workload.*.yml`, tag-driven — see
  `vnext-release-process.md`).
- The **staging profile** is created and uploaded to the staging CDN. This is the
  pre-release channel for internal validation.

Workloads live on the internal feed until promoted. The "Publish to NuGet"
checkbox in the existing pipeline controls whether a package goes to the public
feed or stays internal-only for testing.

> **Open design question:** Can the workload-promotion pipeline reuse existing
> vnext pipeline infrastructure, or does it need new tooling?

### 4.2 SKU completion hook

At the **end of each SKU completing its public cloud release**, a hook triggers
two pipelines in sequence:

```
SKU completes release
  │
  ├─► 1. Workload-promotion pipeline
  │       Inputs: list of workload package IDs + versions to promote
  │       Behavior: promotes each workload from internal feed → public NuGet feed
  │                 (skips if already on public feed — safe for multiple SKUs)
  │
  └─► 2. Profile-update pipeline (runs after promotion succeeds)
          Inputs: profile-name, host-version, bundle-version, worker-versions, etc.
          Behavior: verifies workloads on public feed, then updates registry.json
```

Separating these keeps each pipeline focused and independently reusable:
- The **workload-promotion pipeline** only moves packages between feeds.
- The **profile-update pipeline** only verifies workloads and writes profile data
  to CDN. It can also be invoked outside of a release (e.g. emergency profile
  fix, feature toggle change).

The first SKU to complete deployment promotes the workloads; subsequent SKUs
check for existence and skip promotion.

No hooks between individual UDs are required — only at the end of each SKU
completing its release. Slow and N−1 channel triggers need further design.

### 4.3 Workload promotion

The workload-promotion pipeline ensures all referenced workloads are on the
public NuGet feed before the profile is updated. Workloads are distinct from
cloud-side artifacts (Site Extension / RU package) — the host rollout makes a
version *live in Azure*, but the **workload** is what makes it *installable
locally by the CLI*. A profile must never reference a version that is not yet
available on the target feed.

> **Open design question:** What is the mechanism for promoting a workload from
> internal to public feed? (NuGet push, feed view promotion, or re-queuing the
> existing pipeline with "Publish to NuGet" checked?)

### 4.4 Profile registry update

The profile-update pipeline is an **independent, reusable pipeline** that takes
specific arguments and updates the profile registry in CDN storage.

**Inputs:**

| Parameter | Description |
| --- | --- |
| `profile-name` | The profile to update (e.g. `flex`, `windows-consumption`) |
| `channel` | `staging` or `public` (determines which registry to target) |
| `host-version` | The host version (or range upper bound) to set |
| `bundle-version` | The extension bundle version (range upper bound) to set |
| `worker-versions` | Map of worker runtime → version (e.g. `{python: "4.43.0", node: "3.13.0"}`) |
| `generated-at` | Timestamp for the profile (for staleness detection) |

**Behavior:**

1. **Verify workload availability (precondition).** Query the target NuGet feed
   and confirm every referenced workload version exists. If any is missing,
   **fail with a clear error** — do not update the profile. This makes it
   impossible to publish a broken profile regardless of invocation path.
2. Fetch the target `registry.json` from CDN-backed storage.
3. Locate the profile entry matching `profile-name` (or create if new).
4. Update the version ranges per the input parameters.
5. Write the updated `registry.json` back to storage.
6. Regenerate and upload the detached `registry.json.sha256` checksum.

Multiple SKU pipelines may update the same registry concurrently; the
read-modify-write must use **optimistic concurrency** (e.g. ETag / blob lease).
Each pipeline only touches its own profile entry, so conflicts should be rare.

The `create-host-package` / `host.official` build is the **source of truth** for
the host version range a profile should point at. This pipeline can also be
invoked **outside of a release** (e.g. emergency profile fix, feature toggle
change) without requiring a full host release — this is a key benefit of
keeping it independent and parameterized.

## 5. CDN layout and CLI resolution

The profile registry follows `cli-profiles.md` §6.1: each channel has a **single
`registry.json`** on CDN. The CLI resolves profiles using a three-tier fallback:

```
1. Remote fetch    → CDN registry (1-hour TTL cache)
2. Local cache     → ~/.azure-functions/profiles/registry.json (warn if > 7 days old)
3. Bundled fallback → registry.json shipped with CLI package (point-in-time snapshot)
```

The profile-update pipeline updates the CDN registry independently of CLI
releases. Profiles stay current without requiring a new CLI release — the CLI
picks up changes on its next remote fetch. The bundled copy is just an offline
fallback, refreshed whenever the CLI itself ships.

**GA scope:**

```
https://aka.ms/func-profiles              → registry.json (default / public)
https://aka.ms/func-profiles-sha256       → registry.json.sha256
```

**Post-GA scope (needs design):**

```
https://aka.ms/func-profiles-slow         → registry.json (slow ring)
https://aka.ms/func-profiles-slow-sha256  → registry.json.sha256
https://aka.ms/func-profiles-n1           → registry.json (N−1 / App Service)
https://aka.ms/func-profiles-n1-sha256    → registry.json.sha256
```

Each registry file uses the same schema (`$schema: func-profiles/v1/schema.json`).
The CLI resolves which registry to fetch based on the profile name (e.g. `flex`
→ default, `flex-slow` → slow). The exact naming and resolution convention needs
to be finalized.

Profile data can also be leveraged beyond the CLI (dashboards, public
announcements about runtime versions per SKU, external tooling) since profiles
contain the exact versions of every component across all SKUs.

## 6. Component versioning

### Host

The host uses **roll-forward versioning**: the profile's `host.version` is a
NuGet-style version range (e.g. `[4.1048.0, 4.1049.0)`) and the CLI resolves to
the highest installed or available version within the range. A newer host patch
can satisfy the profile constraint without a profile update.

### Workers

Worker workloads are independently versioned. The authoritative source for worker
versions differs by platform:

- **Linux/Flex:** Pool config lists each language and its image version.
- **Windows:** The git tag + host repo file inspection (limited by GH API rate
  limits — there is a step in the current v4 pipeline that checks worker
  versions by pulling GH releases and inspecting files at that tag).

**Per-stack discrepancies are not common but possible.** A specific stack can 
be held back while others ship. The profile must cap to the
**least common denominator** across all stacks in the SKU, unless per-stack
granularity is added.

Workers release with the host typically, but worker-only patches (same host,
updated worker) are possible and translate to a worker workload update + profile
update. The host build pipelines do **not** emit worker versions — worker
version information comes from the pool config (Linux) or repo inspection
(Windows), not from `host.official` / `create-host-package`.

- **Q:** Can profiles support per-stack granularity so a SKU can release
  bundles/host for all stacks except one held back?

### Extension bundles

On Windows, RU releases Host and Extension Bundles together. The profile's
bundle range advances in lockstep with the host off the same RU build. For
Linux, the pool config is the equivalent source.

- **Exact version ranges, not wildcards.** The upper limit is the exact version
  deployed in the cloud. Every bundles release requires the profile to be updated
  via the SKU completion hook.

## 7. Rollback and hotfix

Consistent with the release story (`cli-release-story.md`,
`vnext-release-process.md`): **roll forward, never retag.**

- **Staging profiles are internal**, so rollbacks before public promotion don't
  touch public profiles.
- **Host rollback** is done by disabling a site-extension version
  (`DisabledSiteExtensionVersions`), per stamp/UD. This is roll-forward-compatible
  but means a **public profile can still pin a disabled host version.** The
  profile must react to host disables/hotfixes.
  - **Q:** How does the profile range max get corrected when the host disables a
    version?
- **Out-of-band profile changes** (feature toggle fix, typo'd range, emergency
  constraint) can be made by invoking the profile-update pipeline directly
  without a full host release. This is a key benefit of having an independent,
  parameterized profile-update pipeline.

## 8. Built-in profiles in the CLI

The CLI ships with a **bundled copy of the profile registry** so it can function
offline or when CDN is unreachable. The resolution order is:

```
1. Remote fetch    → CDN registry (1-hour TTL cache)
2. Local cache     → ~/.azure-functions/profiles/registry.json (warn if > 7 days old)
3. Bundled fallback → registry.json shipped with CLI package (point-in-time snapshot)
```

**Monthly CLI release cadence.** We plan to release the CLI on a monthly cadence.
Before each release, an **agent skill** will fetch the latest public CDN
`registry.json` and update the bundled profiles in the CLI source code (the
workload hosting the built-in registry). This keeps the offline fallback
reasonably fresh without manual intervention.

## 9. Open questions

| # | Question | Status |
| --- | --- | --- |
| 1 | Authoritative source for worker versions per host version? | **Partially resolved:** Pool config for Linux/Flex; git tag inspection for Windows (limited by GH API rate limits). |
| 2 | Where should built-in profiles live — CLI workload or host workload? | Open — affects bundling strategy and offline resolution. |
| 3 | Which SKUs support App Service N−1 channels? Extended to Flex? | Open — affects N−1 registry scope. |
| 4 | Per-stack granularity in profiles? | Open — not frequent but a possibility that stacks are held back. |
| 5 | CDN registry naming and CLI resolution convention (how does CLI know which registry to fetch for a given profile?) | Partially resolved — default registry defined, slow/N−1 need finalization. |
| 6 | Workload promotion mechanism (NuGet push, feed view promotion, or re-queue existing pipeline)? | Open |

## Related docs

- `cli-profiles.md` — profile design and schema.
- `vnext-release-process.md` — component tag + release pipeline mechanics.
- `cli-release-story.md` — overall CLI/workload release philosophy and rollback stance.
- [#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332) — profiles CDN work.
- [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329) — profile update pipeline work.