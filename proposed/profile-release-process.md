# Profile Release Process (Draft)

**Status:** Draft

## Overview

Profiles (`flex`, `windows-consumption`, etc.) are a **constraint set**
that pins the host version range, extension bundle range, worker versions, and feature toggles a
SKU supports (see `cli-profiles.md`). Those constraints are only correct if they
track what is actually deployed to each SKU in the cloud. As the host rolls out
in stages across clouds, the profile registry must be updated and
re-published so the CLI never gets ahead of the host versions a customer's
target SKU is running. The host workload associated with the latest in production must also be available before a SKU profile is released.

This doc captures how a **profile release** happens: when it is triggered, how it
hooks into the host release pipeline, which host rollout milestone gates staging
vs. public, and how we roll back, hotfix, and keep bundles/worker versions in
sync. It builds on the component tag-and-pipeline model in
`vnext-release-process.md` and the design in `cli-profiles.md`.

> This is an early draft. Open questions are called out inline as **Q:** and consolidated in [§Open questions](#open-questions).

## How the host actually rolls out

Before designing the profile release we have to match the host's real rollout
shape:

- **Promotion is manual and adoption-gated.** There is **no automated "rollout reached UD N" event** to
  subscribe a pipeline to.
- **The entire rollout is production.** There is no internal "staging" channel in
  the host rollout. "Staging vs. public" is a *CLI-profile* construct we layer on
  top; it must be defined against host **stages/clouds/adoption**, not against a
  host staging feed.
- **Release axis is host-type + cloud + OS, not SKU name.** The host ships per
  host type (Out-Of-Proc, InProc8, InProc6, V3) and per cloud. **RU is
  Windows-only**; Linux SKUs (CV1/Flex/Dedicated) roll out through separate repos
  and pipelines. **Q:** Is it possible to track each defined SKU we support in the profiles? What can we use to track its progress and gate profile release steps? Which SKUs have pipeline hooks or stages we can take advantage of?

## Release model

```
host.official build ──► create-host-package (Site Extension zip)  ──► create staging host workload (?)
        │
        ▼
  Windows: RU (Host + Extension Bundles) ──► EV2 staged rollout
        Prod S0─S1─…─S5 
  Linux:   separate build/publish pipelines
        │                         │
        ▼                         ▼
  host workload: staging    host workload: public
  profile: staging          profile: public
  (early stage / low         (defined public milestone —
   adoption reached)          e.g. Prod complete)
```

A profile release is **downstream of the host rollout**: the profile registry's
`host.version` range for a SKU should only advance once the host has actually
reached the stage/adoption milestone that gates that SKU's profile channel.

- **Staging vs. public.** Profile updates land on a staging channel first, then
  promote to public once the host reaches the gating milestone.
  - **Q:** Which host milestone gates each channel? Because promotion is manual and
    adoption-based (not UD-numbered), this must be expressed as a **stage/cloud +
    adoption %** (e.g. staging = early Prod stage reached; public = Prod rollout
    complete). 
- **Per-SKU/OS cadence.** Windows and Linux SKUs get host bits from different
  pipelines on different schedules, and the profile registry's per-SKU
  `host.version` must be populated from **both paths** even though they share one
  registry.
  - **Q:** One pipeline updating all SKU entries, or per-path (Windows RU vs Linux)
    pipelines feeding the registry?

## Hooking into the host release pipeline

The profile release should be **driven by the host release**, not maintained by
hand. The host release has well-defined artifact hand-off points we can hook:

```
host.official build
  └─ create-host-package  ──► Site Extension zip + build ID   ◄── version source of truth
  └─ (Windows) RU build ──► rapidupdate.xml (Host + Extension Bundles)
  └─ EV2 staged rollout (manual, adoption-gated promotions)
        └─ hook into Profile release ──► update profile registry
```

- The **`create-host-package` / `host.official` build is the source of truth for
  the version range** a profile should point at. Profile release consumes that
  build's output rather than re-deriving versions.
- **Trigger.** The cleanest automatic trigger is off the **`create-host-package`
  build (or the Windows RU build ID)** — *not* "rollout reached a gating
  UD," since they are not fully automated.
  - **Q:** Do we trigger the profile-registry *update* at package build time and
    hold the *public* promotion until the host reaches its public milestone?

### Prerequisite: the host workload must be published first

A profile only pins a `host.version` range; the CLI resolves that range against
**host workloads** (`Azure.Functions.Cli.Workload.Host`) and auto-installs the
matching version from the workload feed when it isn't already present (see
`cli-profiles.md` §13.1–§13.2). That creates a hard ordering constraint:

- **The host workload package for the new host version must be built and available
  on the CLI's workload feed/CDN *before* any profile advertises that version.** If
  a profile's range advances ahead of the workload, `func start` resolves the
  profile to a version it cannot download — auto-install fails and the profile is
  effectively broken for every consumer on that channel.
- This is a distinct artifact from the cloud-side Site Extension / RU package: the
  host rollout makes the version *live in Azure*, but the **host workload** is what
  makes the same version *installable locally by the CLI*. Both must exist, but it
  is workload availability — not cloud rollout — that gates whether a profile can
  safely name a version.
- Practically, the profile release should verify (or be triggered by) **host
  workload publication**, not just the host build or RU build. Publishing the
  profile registry update and publishing the host workload should be ordered so the
  workload is always the earlier (or same) step.

> No existing doc fully specifies this ordering. `cli-release-story.md` notes only
> that "the host workload will need to align with the host release cadence
> (separate design)"; `cli-profiles.md` §13.2 covers the *runtime* "no matching
> workload" case but assumes the workload is already on the feed. This doc is the
> place to pin the release-time guarantee.

## Worker versions

Each profile (or the host it pins) implies a set of compatible worker versions per
language. We need a deterministic source for "given this host, which worker
versions are valid?"

- The host release produces a **Site Extension package only** — the host build
  pipelines do **not** show worker versions being emitted by `host.official` /
  `create-host-package`. So "worker versions are an output of the host build" is
  **not currently supported** and can't be assumed.
- **Q:** What is the authoritative source for worker versions per host version? This
  is an **open dependency**, not a solved input — options include the host
  build/workload manifest (if extended to emit it) or a separate worker-version
  registry.
  - There is a step in the current v4 pipeline that checks worker versions by pulling GH releases of the host and checking the repo at that tag and inspecting files. This is limited by GH API unauthenticated requests limit.

## Rollback and hotfix

Consistent with the rest of the release story (`cli-release-story.md`,
`vnext-release-process.md`): **roll forward, never retag.**

- **Staging profiles are internal**, so rollbacks/hotfixes that occur before a
  profile is promoted to public need not touch any public profile.
- **Host rollback reaches published versions.** In the cloud, the host is rolled
  back by **disabling a specific site-extension version** via the
  `DisabledSiteExtensionVersions` hosting config, **per stamp/UD**. This is
  roll-forward-compatible (no retag), but it creates a gap the
  profile release must handle: **a public profile can still pin a host version that
  has been disabled in-cloud.** The profile release must react to host
  disables/hotfixes, not just to its own staging state.
  - **Q:** When the host disables a version on some UDs/stamps, how does the profile
    range max get corrected (roll the pinned range forward past the disabled
    version)?

## Extension bundle range

This will not be common because it would depend on a major update of bundles.

On Windows, **RU releases the Host and Extension Bundles together**
through the same pipeline and `rapidupdate.xml`. That
gives a natural answer to "when do we update the bundle range?": the profile's
bundle range can advance **in lockstep with the host, off the same RU
build**.

- **Q:** For Linux (no RU), what is the equivalent joined signal for
  advancing the bundle range alongside the host?

## Profiles outside a release

Most profile changes ride a host release, but some may not (e.g. tightening a
feature toggle, fixing a typo'd range, an emergency constraint).

- **Q:** Do we ever need to change profiles **outside of a release**? If so, what is
  the lightweight path (and how does it interact with the staging → public gating)?

This is the strongest argument for having a separate release pipeline for profilese instead of hooking into existing release pipeliens.

## Built-in profiles in the CLI

Built-in profiles ship as a cached/bundled registry the CLI falls back to offline
(see `cli-profiles.md` §5). Today some of this lives close to the CLI (compare to
how culture/region `.json` resources are embedded).

- **Q:** Where do built-in profiles live — embedded in the CLI, another workload? Do we want/need them? Our order of pulling profilese is CDN, user, then "built-in". 
- **Q:** How often should profiles be released? We can update profiles (merge new/updated profiles) into CLI but hold on releasing and wait for the monthly cadence. This will minimize releases needed (i.e. If Flex releases every week, then we would have to re-release CLI every week to update flex profile).
- This ties to the profiles CDN and profile update pipeline work tracked in
  [#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332) and
  [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329).

## Open questions

| # | Question |
| --- | --- |
| 1 | Do we hook profile releases into existing host pipelines/processes, stand up a **separate profile-release pipeline**, or a mix of both? |
| 2 | Which host **stage/cloud + adoption milestone** gates each profile channel (staging vs. public), and does it differ per SKU/OS? |
| 3 | How do we populate each SKU entry — one pipeline, or per-path (Windows RU vs Linux) pipelines feeding one registry? |
| 4 | Exact trigger for the profile update — `create-host-package`/RU build ID or **host workload publication**, with public promotion held as a manual gate? |
| 5 | Authoritative source for worker versions per host version (open dependency — host build does not currently emit them)? |
| 6 | Bundle range rides the host via RU on Windows — what is the equivalent signal on Linux? |
| 7 | How does a profile react when the host **disables** a published version (`DisabledSiteExtensionVersions`) or ships a hotfix? |
| 8 | Do we ever change profiles outside of a release, and what is the lightweight path? |
| 9 | Where do built-in profiles live — in CLI (current) or should they move (host workload)? |

## Related docs

- `cli-profiles.md` — profile design and schema.
- `vnext-release-process.md` — component tag + release pipeline mechanics.
- `cli-release-story.md` — overall CLI/workload release philosophy and rollback stance.
