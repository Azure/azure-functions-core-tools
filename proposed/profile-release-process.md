# Profile Release Process (Draft)

**Status:** Draft — revised after the CLI Profiles Design Sync (2026-09-25)
and review comments on [#5608](https://github.com/Azure/azure-functions-core-tools/pull/5608).

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
> [§9. Open questions](#9-open-questions).

## 2. Background: how the host rolls out

- **Workload validation is independent of cloud rollout.** Private feeds and
  local profiles allow testing before public package publication or deployment
  to any cloud stage. No additional cloud stage is required for CLI validation.
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

## 3. Profile catalog

**Profiles are named constraint sets**
Users select a profile name; there is no additional channel selector or nested
public/non-public section within a profile. Each environment state that needs
to be represented gets its own named profile in the **same registry**.

| Profile name or proposed pattern | Represents | Updated when |
| --- | --- | --- |
| `flex` | Flex Consumption public deployment. | Flex public release completes. |
| `windows-consumption` | Windows Consumption public deployment. | Windows Consumption public release completes. |
| `windows-dedicated` | Windows Dedicated public deployment. | Windows Dedicated public release completes. |
| `linux-dedicated` | Linux Dedicated public deployment. | Linux Dedicated public release completes. |
| `<sku>-slow` (e.g. `flex-slow`) | An environment grouping with a separate deployment cadence. Not necessarily a fixed offset; versions may match the default profile. | All deployments represented by that profile complete; initially a manual pipeline invocation where needed. |
| Windows release-channel profiles (names TBD) | Windows Rapid Update channel state, including delayed/N−1 releases. | The corresponding channel's state changes, not simply when the default profile advances. |
| `<sku>-preview` (e.g. `flex-preview`, if needed) | A chosen set of early-access versions. | Explicitly published when there is a use case; not required now. |

The current understanding is that the delayed/N−1 channel is a **Windows
Rapid Update feature**, not a channel available on every SKU. The exact SKU
coverage and existing channel names still need confirmation before defining the profile names. Support for this is not required for GA.

Initial implementation will tart with a small catalog and add profiles as needed. The first four profiles named in the table above will likely be the first set.
Where automatic completion hooks are unavailable, a release owner can invoke
the profile-update pipeline manually after the represented deployments finish.
Future orchestration can wait for those deployments and invoke the same pipeline.

Preview versions do not require a new registry or schema concept. A named
preview profile can reference them, or a developer can use a local custom
profile for early testing without claiming alignment with a deployed SKU.

## 4. Release flow

```
host.official build ──► create-host-package (Site Extension zip)
        │
        ├─► cut workloads (host + workers)
        │       └─► validate ──► publish to public NuGet feed
        │                       (no SKU completion prerequisite)
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
              profile-update pipeline
              (requires successful publication of referenced workloads)
```

### 4.1 Workload build, validation, and publication

When the host is cut, build the host and related worker workloads using the
existing per-component release pipelines
(`eng/ci/release/official-release.workload.*.yml`, tag-driven — see
`vnext-release-process.md`). Validate and publish them **without waiting for any
SKU to complete, or even start, deployment**. Workload publication belongs with
the workload release flow, not the SKU completion hook.

Private/internal feeds remain available for validation before bits are public.
The existing "Publish to NuGet" pipeline option controls public publication;
leaving it unchecked permits internal-only testing.

Publishing a package makes it available but does **not** advance any deployed-SKU
profile. Explicit upper bounds keep early packages out of those profiles until
their version constraints are updated. Partners can opt into early bits through
a local/custom profile or explicit versions.

There is **no required staging profile on CDN**. Local profiles and private
feeds are sufficient for validation; a shared preview profile can be added
later if needed.

### 4.2 SKU completion hook

At the **end of each SKU completing its public cloud release**, an integrated
step calls the standalone profile-update pipeline:

```
SKU completes release
  │
  └─► Profile-update pipeline
          Inputs: profile-name, version constraints, represented state
          Gate: successful publication of referenced workload versions
          Behavior: updates that profile in registry.json
```

The same pipeline accepts manual invocations for environments without an
automated hook, new profiles, emergency corrections, and feature changes.
If a profile represents several deployments, update it only after all of them
complete. No hooks between individual UDs are required.

### 4.3 Publication gate

A profile must not advertise workloads that have not been published. However,
release pipelines cannot verify a fresh public NuGet push by reading it back:
reads must go through Microsoft-approved feeds with a **seven-day quarantine**.
An immediate feed query would therefore reject newly published packages.

Use successful package-push results as publication evidence, following the
existing release-pipeline practice described in
[review](https://github.com/Azure/azure-functions-core-tools/pull/5608#discussion_r4107227022).
The gate must cover the package IDs and versions referenced by the update, not
just an unrelated successful pipeline run. A failed or unverified publication
must block the profile update; a manual invocation must not bypass this gate.

- **Q:** How will publication results be recorded and supplied to the standalone
  pipeline, including manual updates and references to previously published
  packages? The handoff and validation contract still need implementation design.

### 4.4 Profile registry update

The profile-update pipeline is an **independent, reusable pipeline** that takes
specific arguments and updates the profile registry in CDN storage.

**Inputs:**

| Parameter | Description |
| --- | --- |
| `profile-name` | The profile to update (e.g. `flex`, `windows-consumption`) |
| `host-version` | Bounded host version range (e.g. `[4.1048.0, 4.1048.200]`) |
| `bundle-version` | Bounded extension bundle version range (e.g. `[4.30.0, 4.35.0]`) |
| `worker-versions` | Map of worker runtime → bounded workload version range (e.g. `{python: "[4.42.0, 4.43.0]", node: "[3.12.0, 3.13.0]"}`) |
| `generated-at` | Timestamp for the profile (for staleness detection) |

The profile name selects an entry in the shared registry, not a CDN endpoint.
The invocation also needs the state being represented (such as feature and
runtime constraints when applicable) and publication evidence from §4.3;
the exact input contract for those remains to be defined.

**Behavior:**

1. **Validate constraints and publication evidence (precondition).** Require
   bounded version ranges and successful publication evidence for the referenced
   workloads, without an immediate public-feed read. Fail clearly without
   changing the registry if the precondition cannot be established.
2. Fetch the target `registry.json` from CDN-backed storage.
3. Locate the profile entry matching `profile-name` (or create if new).
4. Update the version ranges and any supplied feature/runtime constraints.
5. Write the updated `registry.json` back to storage.
6. Regenerate and upload the detached `registry.json.sha256` checksum.

Multiple SKU pipelines may update the same registry concurrently; the
read-modify-write must use **optimistic concurrency** (e.g. ETag / blob lease).
Each pipeline only touches its own profile entry, so conflicts should be rare.

The host build identifies available bits; the **completed deployment state**
determines what a deployed-SKU profile should reference. Merely finishing
`host.official` / `create-host-package` does not advance that profile.

## 5. CDN layout and CLI resolution

The profile registry follows `cli-profiles.md` §6.1: **one `registry.json`**
contains the named profiles, including slow or preview profiles if introduced.
The CLI resolves profiles using a three-tier fallback:

```
1. Remote fetch    → CDN registry (1-hour TTL cache)
2. Local cache     → ~/.azure-functions/profiles/registry.json (warn if > 7 days old)
3. Bundled fallback → registry.json shipped with CLI package (point-in-time snapshot)
```

The profile-update pipeline updates the CDN registry independently of CLI
releases. Profiles stay current without requiring a new CLI release — the CLI
picks up changes on its next remote fetch. The bundled copy is just an offline
fallback, refreshed whenever the CLI itself ships.

**Shared endpoint:**

```
https://aka.ms/func-profiles              → registry.json
https://aka.ms/func-profiles-sha256       → registry.json.sha256
```

The registry uses the existing schema (`$schema: func-profiles/v1/schema.json`).
Selecting `flex` or `flex-slow` selects a different entry, not a different
endpoint.

**Composition:** A base profile can hold shared features and flags, while derived
profiles override component versions for a specific environment. Such base
profiles are not intended as user-facing targets, so the number of registry
entries can exceed the public profile catalog.

**Catalog growth:** Start small; adding profiles is easier than removing names
that users reference. If registry size becomes a problem, consider references
to separate CDN profile documents and a deprecation/removal policy. That is a
future design option, not a requirement to split the registry now.

Profile data can also be leveraged beyond the CLI (dashboards, public
announcements about runtime versions per SKU, external tooling) since profiles
describe the supported component versions for the environments they represent.

## 6. Component versioning

### Bounded constraints

Host, worker, and bundle constraints use **NuGet-style version ranges**, not
unqualified "latest" values. Published environment profiles must specify an
explicit upper bound matching the supported deployment state and a lower bound
where applicable. Although version-expression syntax is flexible, **do not use
wildcards or unbounded ranges in these profiles**: they could admit early
packages before the represented environment supports them.

Illustrative constraints (not a statement of current deployment versions):

| Component | Constraint | Meaning |
| --- | --- | --- |
| Host | `[4.1048.0, 4.1048.200]` | Permit versions from 4.1048.0 through the deployed maximum 4.1048.200, inclusive. |
| Bundles | `[4.30.0, 4.35.0]` | Require at least 4.30.0; do not admit versions above 4.35.0. |
| Python worker workload | `[4.42.0, 4.43.0]` | Permit workload package versions within the supported bounds. |
| Pinned worker workload | `[4.42.0]` | Permit only this version, for example during a targeted rollback. |

Worker workload package versions are distinct from language versions such as
Python 3.x. Constraints must refer to the versioned component being selected.
A frozen environment keeps its cap until its supported state changes; a new
package in NuGet alone never justifies raising it.

### Host

The host uses **roll-forward versioning**: the profile's `host.version` is a
NuGet-style version range and the CLI resolves to the highest installed or
available version within that range. Roll-forward does not bypass the upper
bound. A hotfix above the cap requires an explicit profile update after the
represented deployment supports it.

### Workers

Worker workloads are independently versioned. The authoritative source for worker
versions differs by platform:

- **Linux/Flex:** Pool config lists each language and its image version.
- **Windows:** The git tag + host repo file inspection (limited by GH API rate
  limits — there is a step in the current v4 pipeline that checks worker
  versions by pulling GH releases and inspecting files at that tag). NOTE: An alternative would be preferred.

**Per-stack discrepancies do not automatically hold back every worker.** Use
the affected worker's constraint to retain a known-good version or pin it while
other workers advance. If a stack is unavailable in a host release, the
host-to-stack mapping must represent that rather than advertising support.

Workers release with the host typically, but worker-only patches (same host,
updated worker) are possible and translate to a worker workload update + profile
update. The host build pipelines do **not** emit worker versions — worker
version information comes from the pool config (Linux) or repo inspection
(Windows), not from `host.official` / `create-host-package`.

For example, the [review scenario](https://github.com/Azure/azure-functions-core-tools/pull/5608#discussion_r4065319740)
maps host 4.1050 either to a known-good older Python worker or to no Python
support, while host 4.1049 retains its Python mapping.

Use worker constraints for targeted exclusions rather
than freezing the entire release.

- **Q:** How should host-to-stack availability and non-contiguous language
  exclusions be represented and resolved when a worker range or exact pin is
  insufficient?

### Extension bundles

On Windows, RU releases Host and Extension Bundles together. The profile's
bundle range advances in lockstep with the host off the same RU build. For
Linux, the pool config is the equivalent source.

- **Exact version ranges, not wildcards.** The upper limit is the exact version
  supported in the represented environment. Publishing a bundle early does not
  advance the profile; update its bounds when that environment's release completes.

## 7. Rollback and hotfix

Consistent with the release story (`cli-release-story.md`,
`vnext-release-process.md`): **roll forward, never retag.**

- **Before a profile update**, rolling back the cloud release leaves the
  existing profile unchanged. Published workloads can remain on NuGet.
- **After a profile update**, reset the affected constraints to known-safe
  versions that match the rolled-back environment. Narrow the range or pin a
  safe version as needed; do not leave the bad version eligible.
- **Host rollback** is done by disabling a site-extension version
  (`DisabledSiteExtensionVersions`), per stamp/UD. This is roll-forward-compatible
  but means a **public profile can still pin a disabled host version.** The
  profile must be corrected as part of the rollback response.
- **Delisting is optional, not the rollback mechanism.** An explicitly referenced
  package version can still be restored after delisting. Profile constraints
  are what prevent that version being selected through the profile.
- **Out-of-band profile changes** (feature toggle fix, typo'd range, emergency
  constraint) can be made by invoking the profile-update pipeline directly
  without a full host release, subject to the same publication gate.

- **Q:** Who owns the rollback-triggered profile correction, and which disable
  or rollback signals can invoke it automatically?

## 8. Built-in profiles in the CLI

The CLI ships with a **bundled copy of the profile registry** so it can function
offline or when CDN is unreachable, using the fallback order in §5.

**Monthly CLI release cadence.** We plan to release the CLI on a monthly cadence.
Before each release, an **agent skill** will fetch the latest public CDN
`registry.json` and update the bundled profiles in the CLI source code (the
workload hosting the built-in registry). This keeps the offline fallback
reasonably fresh without manual intervention.

## 9. Open questions

| # | Question | Status |
| --- | --- | --- |
| 1 | How will publication results be recorded and supplied to the standalone profile-update pipeline? | Open — define the evidence handoff and validation contract, including manual updates and previously published packages (§4.3). |
| 2 | What alternative can replace Windows git tag and host repo inspection for worker versions? | Open — prefer an approach that avoids the current GitHub API rate-limit constraints (§6, Workers). |
| 3 | How should host-to-stack availability and non-contiguous language exclusions be represented and resolved? | Open — define the cases where a worker range or exact pin is insufficient (§6, Workers). |
| 4 | Who owns rollback-triggered profile corrections, and which signals can invoke them automatically? | Open — identify the owner and disable/rollback triggers (§7). |

## Related docs

- `cli-profiles.md` — profile design and schema.
- `vnext-release-process.md` — component tag + release pipeline mechanics.
- `cli-release-story.md` — overall CLI/workload release philosophy and rollback stance.
- [#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332) — profiles CDN work.
- [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329) — profile update pipeline work.