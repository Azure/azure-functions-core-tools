# Profile Release Process (Draft)

**Status:** Draft

## Overview

Profiles (`flex`, `windows-consumption`, etc.) are a **constraint set**
that pins the host version range, extension bundle range, worker versions, and feature toggles a
SKU supports (see `cli-profiles.md`). Those constraints are only correct if they
track what is actually deployed to each SKU in the cloud. As the host rolls out
across update domains (UDs), the profile registry must be updated and
re-published so the CLI never gets ahead of the host versions a customer's
target SKU is running.

This doc captures how a **profile release** happens: when it is triggered, how it
hooks into the host release pipeline, which UD gates staging vs. public, and how
we roll back, hotfix, and keep bundles/worker versions in sync. It builds on the
component tag-and-pipeline model in `vnext-release-process.md` and the design in
`cli-profiles.md`.

> This is an early draft. Open questions are called out inline as **Q:** and consolidated in [§Open questions](#open-questions).

## Release model

```
New host tagged ──► release ──► UD0 ─► UD1 ─► ... ─► UD6 ─► ... (rolls across UDs)
                                         │              │
                                         ▼              ▼
                                   profile: staging  profile: public
```

A host version rolls out gradually across update domains. A profile release is
**downstream of the host rollout**: the profile registry's `host.version` range
for a SKU should only advance once the host has actually reached the UD that gates
that SKU's profile.

- **Staging vs. public.** Profile updates land on a staging channel first, then
  promote to the public channel once the corresponding UD has rolled.
  - **Q:** Which UD gates each channel? Whiteboard starting point: **UD1 → staging**,
    **UD6 → public**. Confirm these are the right gates and whether they differ per SKU.
- **Per-SKU cadence.** Different SKUs get host bits at different times (Flex first,
  then Consumption, then Windows), so each SKU's profile entry advances on its own
  schedule even though they share one registry.
  - **Q:** Does each SKU/release have its own pipeline, or one pipeline that updates
    all SKU entries? (Whiteboard: "does each release have its own pipeline?")

## Hooking into the host release pipeline

The profile release should be **driven by the host release**, not maintained by
hand. When the host workload is built and released, that signal should flow into a
profile-update step.

```
Host (Workload) ─ CI/CD
  └─ trigger when?
  └─ when does the host go into the model / registry?
  └─ upload to staging feed
  └─ host + workload go into the staging feed together
  └─ trigger workload release ──► hook into Profile release ──► update profile registry
```

- The host build is the **source of truth for the version range** a profile should
  point at. Profile release consumes the host build's output rather than
  re-deriving versions.
- **Q:** What is the exact trigger? When the host workload uploads to the staging
  feed, when it's tagged, or when the rollout reaches a gating UD?
- **Q:** Do host and workload always go into the staging feed together, and does
  the workload release trigger the profile update automatically?

## Worker versions

Each profile (or the host it pins) implies a set of compatible worker versions per
language. We need a deterministic source for "given this host, which worker
versions are valid?"

- **Q:** Where do we get worker versions for a given host? Starting assumption:
  it's an **output of the host build**, emitted alongside the host version so the
  profile-release step can populate the registry without a separate lookup.

## Rollback and hotfix

Consistent with the rest of the release story (`cli-release-story.md`,
`vnext-release-process.md`): **roll forward, never retag.**

Staging-level profiles are entirely internal, so if any rollbacks or hotfixes are required before the completion of UD6, we do not have to be concerned with fixing any public profiles.

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
- **Q:** How often should profiles be released? We can update profiles (merge new/updated profiles) into CLI but hold on releasing and wait for the monthly cadence. This will minimize releases needed (i.e. If flex releases every week, then we would have to re-release CLI every week to update flex profile).
- This ties to the profiles CDN and profile update pipeline work tracked in
  [#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332) and
  [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329).

## Open questions

| # | Question |
| --- | --- |
| 1 | Which UD gates each profile channel? |
| 2 | How do we update each SKU — one pipeline or per-SKU/per-release? |
| 3 | Exact trigger for the profile update off the host release? | 
| 4 | Where do worker versions for a given host come from? |
| 5 | When do we update the bundle range in a profile? |
| 6 | Do we ever change profiles outside of a release? |
| 7 | Where do built-in profiles live — in CLI (current) or should they move (host workload)? |

## Related docs

- `cli-profiles.md` — profile design and schema.
- `vnext-release-process.md` — component tag + release pipeline mechanics.
- `cli-release-story.md` — overall CLI/workload release philosophy and rollback stance.
