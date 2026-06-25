# Core Tools & Functions CLI Release Design

> **Status:** Draft. This document proposes how Azure Functions Core Tools
> ships v4 and v5 in parallel: release cadence, workload alignment with the
> core CLI, the per-channel distribution matrix, and the rollback story.
> It resolves [#5328](https://github.com/Azure/azure-functions-core-tools/issues/5328).

## Summary of Decisions

| # | Decision | Outcome |
| --- | --- | --- |
| 1 | Distribution philosophy | CDN-first. Install scripts depend on CDN; GitHub Releases are also published. Release pipeline pushes artifacts to CDN. Package managers wrap the CLI binary only; everything else is workloads on NuGet. |
| 2 | v4 + v5 cadence | Independent. v4 enters maintenance mode at v5 GA (EOL: ~1 year, TBD). CLI releases independently from host. Expect higher release frequency initially, tapering as features shift to workloads. |
| 3 | Channel matrix at GA | GA-blocking: install scripts (Linux signing blocker), Homebrew (Mac), winget (Windows, MSI may be needed), npm (cross-plat). Post-GA: APT (Linux). All other channels removed. Single install location recommended to avoid version conflicts. |
| 4 | npm | Both `azure-functions-cli` and `azure-functions-core-tools` publish v5. Use publisher profile (can also use ESRP release). |
| 5 | Rollback | Roll-forward-first, never retag. Workloads are unlisted, never deleted. Remove unused distribution channels. |

## 1. Summary

v4 is a single, monolithic CLI. v5 is a thin, stable CLI whose functionality
ships as independently versioned **workloads** acquired on demand from NuGet.
The two will ship side by side for a transition window, then v5 becomes the
default (`vnext` becomes `main`, today's `main` is renamed to a v4 branch).

The design is **CDN-first**: the `install.sh` / `install.ps1` scripts
depend on CDN for release artifacts. GitHub Releases are also used and
artifacts are published there. Every package manager (npm, winget, Homebrew)
is a thin wrapper that delivers only the CLI binary. All language and runtime
content stays in workloads on NuGet.

## 2. Goals

- Define a v5 release process that works while v4 is still supported.
- Align independently versioned workloads (across owner repos) with a single
  CLI version users can reason about.
- Decide, per channel, whether and how v5 is distributed.
- Define a rollback (or roll-forward) response per channel.
- Surface the implementation work as follow-up issues (§11).

## 3. Non-goals

- The GA cutover mechanics themselves: switching the default branch, renaming
  the v4 branch, and cutting `v5.0.0`. Tracked in
  [#5363](https://github.com/Azure/azure-functions-core-tools/issues/5363).
- The workload ownership migration to per-language repos. Tracked in
  [#5346](https://github.com/Azure/azure-functions-core-tools/issues/5346).
  This design assumes that end state an is focused on the core CLI release itself.
- Profiles CDN and profile update pipelines. Tracked in
  [#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332)
  and [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329).
- The detailed per-component tag and pipeline mechanics, already specified in
  `vnext-release-process.md`. This doc references that as the workload layer.

## 4. Background: how we release today

### 4.1 v4 (`main`): monolithic, fully automated

- One CLI, one version, one `release_notes.md`.
- Bump version and notes, cut a tag from `main`. The official pipeline runs,
  then the consolidated pipeline, then the release pipeline on that tag.
- The release pipeline does everything: creates the GitHub release, uploads
  artifacts, updates the internal tooling feed, and publishes to every
  distribution channel (npm, Chocolatey, apt/deb, MSI, Homebrew) via the
  `eng/tools/publish-tools` driver.

### 4.2 v5 (`vnext`): component/workload model, preview

- The CLI is thin and stable. Functionality ships as independently versioned
  workloads: host, bundles, templates, workers, language stacks, abstractions.
- Each workload is released by pushing a per-component tag from `vnext` and
  queuing its release pipeline (`eng/ci/release/official-release.workload.*.yml`).
  The pipeline packs the workload and publishes a `.nupkg` to a NuGet feed
  (staging `public/pre-release`, with a partner drop). Users acquire workloads
  with `func workload install`. See `vnext-release-process.md`.
- The CLI binary is distributed by `install.sh` / `install.ps1`
  (`https://aka.ms/func-cli/install.sh`), which resolves the latest `v5.*`
  GitHub Release and downloads `func-{os}-{arch}.tar.gz`. The CLI release is
  **not yet fully automated**: the GitHub release and artifact upload are done
  by hand today.
- v5 has no npm, Homebrew, Chocolatey, apt, MSI, winget, scoop, or rpm
  presence yet.

### 4.3 Where this is heading

Workloads move out of core-tools into owner repos (python worker to the
python repo, templates to the templates repo, and so
on, per #5346). A v5 release therefore becomes a cross-repo problem:
each owner repo publishes its workload to NuGet on its own cadence, and
tne core-tools repo owns the CLI release (and SDK/Abstractions).

## 5. Decision 1: CDN-first distribution

**Decision.** Install scripts depend on **CDN** for release artifacts. GitHub
Releases are also used — the release pipeline will output artifacts there as
well. The release pipeline must be updated to push artifacts to CDN.

Package managers are thin wrappers that fetch the CLI binary. They never
bundle language or runtime content; that always comes from workloads on NuGet.

**Why.**

- The v5 CLI is a small native binary. CDN gives reliable, geo-distributed
  delivery that the install scripts can depend on without GitHub rate limits.
- GitHub Releases provide a secondary source and a visible release history.
- Keeping package managers as thin wrappers makes adding a channel cheap and
  keeps every channel consistent.
- It cleanly separates the two distribution problems: the CLI binary (a
  handful of platform archives) and workloads (NuGet packages, on demand).

**Implications.**

- The release pipeline must be updated to push signed artifacts to CDN.
- Each package-manager channel only needs: the CDN asset URL, a checksum, and
  an auto-bump step on release. No channel ever needs to understand workloads.
- **Single install location:** Users should install from one source (e.g. only
  brew or only npm, not both) to avoid ending up with different versions of
  the CLI on their PATH.

## 6. Decision 2: Parallel cadence and v4 posture

**Decision.**

- v4 and v5 release on **independent** cadences. No shared release-day, no
  coupled version numbers. They live on separate branches, architectures, and
  pipelines.
- v4 enters **maintenance mode** when v5 GAs.
  - EOL: ~1 year after v5 GA (exact date TBD).
  - Scope of security releases in core-tools needs to be defined clearly for
    external comms.
- The CLI releases **independently from the host**. The host workload will
  need to align with the host release cadence (separate design).
- v5 will likely release more frequently early on as features are added.
  Eventually the CLI will be mostly maintenance as the majority of changes
  shift to workloads.

**Why.** Coupling two differently shaped products to one schedule adds
coordination cost with no user benefit, and v4 is winding down. A maintenance
-only posture lets the team put its weight behind v5 while keeping v4 users
safe.

**Open items.**

- Define exact v4 EOL date.
- Define scope of "security release" for core-tools (what's in vs out).
- Host workload release alignment is a separate design exercise.

## 7. Decision 3: Distribution channel matrix

**Decision.** The following channels are supported for v5. "Ships" is always
just the CLI binary; workloads come from NuGet regardless of channel.

**Important:** Users should install from a **single source** to avoid ending
up with different versions of the CLI on their PATH (e.g. don't mix npm and
Homebrew installs).

### GA-blocking channels

| Channel | Notes |
| --- | --- |
| install.sh / install.ps1 | Primary install scripts. **Linux signing is a blocker.** Validate all supported os/arch. |
| Homebrew (Mac) | Auto-bump formula on release. |
| winget (Windows) | Auto-submit manifest on release. MSI might be needed and is not trivial. |
| npm (cross-platform) | `azure-functions-cli` = v5, `azure-functions-core-tools` = v5. Need to use publisher profile (can also use ESRP release). |

### Post-GA channels

| Channel | Notes |
| --- | --- |
| APT (Linux) | Thin CLI .deb in MS apt repo. Repackage binary only. |

All other channels (Chocolatey, MSI, Scoop, RPM, internal tooling feed) are
**removed** from the v5 distribution plan.

## 8. Decision 4: Rollback story

**Decision.** Roll-forward-first. We do not retag or mutate a shipped
version; a bad release is fixed by shipping the next patch. Remove unused
distribution channels from rollback planning.

| Channel | Normal fix | Emergency hatch |
| --- | --- | --- |
| CDN / GitHub Releases | Ship next patch | Mark the bad release as pre-release so the installer skips it; never delete assets users may have pinned. |
| install.sh / ps1 | Resolves next stable automatically | Pin guidance: `VERSION=<last-good>`; mark the bad release pre-release. |
| npm | Publish next patch | `npm deprecate` the bad version and move `latest` back to the last good. Never unpublish (npm policy). |
| Homebrew | Bump formula | Revert the formula to the previous revision. |
| winget | New manifest | Submit a manifest revert to the previous version. |
| apt | Publish next patch | Re-point the repo metadata to the previous package. |
| Workloads (NuGet) | Publish next patch | **Unlist** (never delete) the bad version so new resolves skip it, while environments that already installed it can still restore. |

**Why.** Roll-forward keeps release history immutable and reproducible.
Unlist-not-delete on NuGet is the key rule: it protects any environment that
already installed a now-bad workload version from a broken restore, while
stopping new installs from resolving it.

## 9. Testing & OGFs

- Automated smoke tests for v5.
- No need for Watchtower.
- Tests run on all supported distributions.
- **TODO:** Document the test matrix (what we validate, on which os/arch).
- **TODO (Lilian):** Review OGFs for v4.

## 10. Follow-up issues

Implementation items this design surfaces.

**Release pipeline**

1. Update the release pipeline to push artifacts to CDN.
2. Fully automate the v5 CLI release pipeline: auto-create the GitHub Release,
   upload signed `func-{os}-{arch}` archives + checksums. (Extends
   [#5331](https://github.com/Azure/azure-functions-core-tools/issues/5331).)
3. CLI archive signing + macOS notarization in the release pipeline.
4. Linux signing (GA blocker for install scripts).

**Channels (GA-blocking)**

5. npm v5 package: publish both `azure-functions-cli` and
   `azure-functions-core-tools` at v5 using publisher profile / ESRP release.
6. winget manifest for v5 with auto-submit on release. Investigate MSI
   requirement.
7. Homebrew formula/tap for v5 with auto-bump on release.
8. Installer coverage validation matrix (all supported os/arch) as a GA gate.

**Channels (post-GA)**

9. APT/.deb packaging for the v5 thin CLI + Microsoft apt repo publishing.

**Policy and comms**

10. v4 maintenance-mode announcement at v5 GA. Define EOL date (~1 year).
    (Maps to
    [#5359](https://github.com/Azure/azure-functions-core-tools/issues/5359).)
11. Define scope of security releases in core-tools for clear external comms.
12. Per-channel rollback playbook + tooling (deprecate / unlist /
    mark-prerelease / revert runbooks).

**Separate design work**

13. Workload coordination model and CLI/workload alignment (moved to separate
    issue — not a release concern).

## 11. Open questions

- Exact v4 EOL date after v5 GA.
- Scope of "security release" for core-tools — what's included vs excluded.
- Confirm MSI can be dropped for v5 or if winget requires it (enterprise blocker).
- Host workload release alignment with host cadence (separate design).

## 12. References

- Issue: [#5328 Design: v4 + v5 release story](https://github.com/Azure/azure-functions-core-tools/issues/5328)
- `vnext-release-process.md` (per-component tag and pipeline mechanics)
- `v5-ga-plan.md` (GA milestones and release-process notes)