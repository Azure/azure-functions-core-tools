# Core Tools & Functions CLI Release Design

> **Status:** Draft. This document proposes how Azure Functions Core Tools
> ships v4 and v5 in parallel: release cadence, workload alignment with the
> core CLI, the per-channel distribution matrix, and the rollback story.
> It resolves [#5328](https://github.com/Azure/azure-functions-core-tools/issues/5328).
> Each section states a recommendation, the rationale, and any open
> questions. Decisions are not final until the reviewers in §12 sign off.

## 1. Summary

v4 is a single, monolithic CLI. v5 is a thin, stable CLI whose functionality
ships as independently versioned **workloads** acquired on demand from NuGet.
The two will ship side by side for a transition window, then v5 becomes the
default (`vnext` becomes `main`, today's `main` is renamed to a v4 branch).

The design is **installer-first**: the `install.sh` / `install.ps1` scripts
plus GitHub Releases are the primary, fully supported path for the v5 CLI.
Every package manager (npm, winget, Homebrew, apt, Chocolatey) is a thin
wrapper that delivers only the CLI binary. All
language and runtime content stays in workloads on NuGet.

> Open Q: Do we want to depend on GH releases or do we want to deploy to CDN like we do for v4 today?

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

## 5. Decision 1: Installer-first distribution

**Recommendation.** Treat the install scripts plus GitHub Releases as the
primary, fully supported path. Package managers are thin wrappers that fetch
the same GitHub Release binary. They never bundle language or runtime
content; that always comes from workloads on NuGet.

**Why.**

- The v5 CLI is a small native binary. A single signed artifact per
  `{os}-{arch}` on GitHub Releases is the natural source of truth, and the
  installer already consumes exactly that.
- Keeping package managers as thin wrappers makes adding a channel cheap (it
  points at an existing release asset) and keeps every channel consistent.
- It cleanly separates the two distribution problems: the CLI binary (a
  handful of platform archives) and workloads (NuGet packages, on demand).

**Implications.** Each package-manager channel only needs: the release asset
URL, a checksum, and an auto-bump step on release. No channel ever needs to
understand workloads.

> Open Q: Do we depend on GH releases or use the CDN for our release artifacts like we
> do in v4 today?

## 6. Decision 2: Parallel cadence and v4 posture

**Recommendation.**

- v4 and v5 release on **independent** cadences. No shared release-day, no
  coupled version numbers. They live on separate branches, architectures, and
  pipelines.
- v4 is **maintenance-only**: security fixes and critical bug fixes only. New
  features are v5-only. A fix flows v5 to v4 only when it is security or
  critical.
- v5 ships on a regular train plus on-demand patches. Recommended starting
  cadence: a predictable monthly minor train, with patch releases as needed.
  Confirm the exact cadence with release owners (open question).

**Why.** Coupling two differently shaped products to one schedule adds
coordination cost with no user benefit, and v4 is winding down. A maintenance
-only posture lets the team put its weight behind v5 while keeping v4 users
safe.

**Dependencies.** Maintenance-only requires a public deprecation timeline and
a stated backport policy, plus PM sign-off. Tracked in
[#5359](https://github.com/Azure/azure-functions-core-tools/issues/5359).

> Open Q: How often do we want to release the core CLI? Weekly? Monthly
> We can always release ad-hoc + patch, but do we want to commit to a monthly cadance?

## 7. Decision 3 + 4: Coordination model and workload alignment

This is the core of the design: how a fleet of independently versioned,
cross-repo workloads stays aligned with a single CLI users can reason about.

### 7.1 How alignment actually works (no CLI-shipped manifest)

**Recommendation.** Keep the CLI a thin anchor: resolve compatibility through
per-workload metadata and profiles, not a CLI-shipped manifest or bill of
materials. Workloads are acquired on demand by the user, never auto-installed
silently by the CLI:

- `func workload install <name>` resolves the **latest compatible stable**
  version from the catalog (NuGet) by default. `func workload update` moves an
  installed workload to the latest compatible version. An explicit `--version`
  overrides.
- `func setup --feature <...>` orchestrates those installs for a ready
  machine, and `func start --profile <name>` selects the host version to run.

Today the host/workload boundary is **defined but not version-gated**.
Workloads bind to the CLI's single shipped copy of the
contract assemblies (`Azure.Functions.Cli.Abstractions`, the DI abstractions,
and transitionally `System.CommandLine`); `WorkloadLoadContext` delegates
exactly those back to the CLI's load context, and the .NET runtime's load
rules handle target-framework compatibility. What is not in place is any
version-compatibility check. The "workload too old, run `func workload update`"
behavior described in workload-spec §10.2 is specified but not yet implemented
(the install, resolve, and load paths reject only on bad schema and
path-escape, never on version). So the CLI currently loads whatever workload it
is given and only fails at runtime if the contract genuinely mismatches.

**The concern: incompatibility is ungated, especially forward.** With no version
check, a mismatched workload and CLI is caught only at runtime, if at all. The
sharp edge is the forward direction: when a new capability is added to the shared
CLI base contract (the `Azure.Functions.Cli.Abstractions` package the CLI and
workloads compile against), a newer workload that uses it needs a newer CLI.
Nothing today lets that workload declare "I require `func >= X`", so an older CLI
installs it and fails at load with no clear reason. The reverse direction (an old
workload on a newer CLI) is equally ungated.

**Proposal: a minimum CLI version, stamped by the Workload SDK.** Introduce a
declared **minimum CLI (contract) version** per workload, enforced by the
resolver and loader. Three parts:

1. **Declaration (where the Workload SDK helps).** The workload states the
   minimum CLI / contract version it needs. The exact form is an open question in
   the workload specs (workload-package-layout §6 and §11): a `workload.json`
   field, a reserved tag such as `func-cli-min:5.2`, or a NuGet `<dependencies>`
   range on the contract package (which §9.3 discourages). The planned
   **Func Workload SDK** (`Azure.Functions.Cli.Workload.Sdk`) is the natural place
   to derive and stamp this automatically from the `Abstractions` version the
   workload built against, so authors do not hand-author it. The SDK is build-time
   only: it makes the declaration accurate but cannot enforce anything on a user's
   machine.
2. **Enforcement with a clear upgrade path.** `func workload install` / `update`
   resolves the newest version whose minimum CLI is satisfied by the running CLI.
   When a newer version exists but needs a newer CLI, the CLI installs the newest
   compatible version and tells the user that a newer one requires `func >= X`
   (for example, run `func update`), instead of failing opaquely. This is the
   runtime half the SDK cannot provide, and the counterpart to the (today
   unimplemented) "workload too old" path.
3. **Release coordination.** The CLI that carries the new contract capability must
   reach the distribution channels **before or together with** the workload that
   depends on it, so users have an upgrade path the moment that workload ships,
   and the workload's release notes must state the minimum `func` version. Because
   `Azure.Functions.Cli.Abstractions` is itself a separately released, versioned
   package (its own pipeline on `vnext`), a base-contract change is sequenced as:
   abstractions release, then a CLI release that ships it, then the dependent
   workload (see §7.2).

**What "v5.x" means.** Just the CLI version. Workloads and profiles version
independently and can even republish without a `func` release. The CLI is the
thin, stable anchor; everything else is acquired and resolved against it.

**Release-design implications.**

- The minimum-CLI proposal needs a committed declaration form (ideally
  SDK-stamped) and defined resolver behavior before GA. Until then, forward and
  reverse incompatibility is a known, ungated gap (tracked as an open question);
  once it lands, validating a workload's declared minimum becomes a release gate
  in each owner repo, not a core-tools artifact.
- Built-in profiles must be published and updated in step with host/bundle
  releases so a fresh `func setup` / `func start --profile` yields a tested,
  cloud-aligned combination. Owned by #5329 and #5332.

### 7.2 CLI-anchored release coordination

**Recommendation.** A v5 release is **anchored by the CLI release event**:
the GitHub Release (signed, versioned CLI binaries) and the installer land
together as the single source of truth for what shipped. Sequencing:

1. Owner repos publish the workload versions they want available to NuGet,
   ahead of time. Workloads are continuously available; they do not wait for
   the CLI.
2. The CLI release builds and signs the CLI binaries and publishes the GitHub
   Release. This is the anchored moment the release "exists". Updated built-in
   profiles (if any) publish through their own pipeline (#5329).
3. Package-manager channels fan out from that GitHub Release: automated and
   same-day where we control the channel (npm, Homebrew, winget, apt),
   best-effort where submission is gated by a third party.

**Why.** The workloads already exist on NuGet before the CLI release, so the
CLI release never blocks on cross-repo timing. Anchoring on one event gives a
single, unambiguous "this shipped" signal while letting downstream channels
trail without holding the release.

**Implications.** We need a cross-repo release-readiness checklist (the
workloads a release depends on are published, smoke-tested, and declare
correct CLI-compatibility windows before the CLI release goes out) and
aggregated release notes that pull each workload's notes into the CLI release
notes.

## 8. Decision 5: Distribution channel matrix

Per-channel decision for v5. "Ships" is always just the CLI binary; workloads
come from NuGet regardless of channel.

| Channel | v4 today | v5 plan | GA-blocking | Notes |
| --- | --- | --- | --- | --- |
| install.sh / install.ps1 | n/a | Primary | Yes | Source of truth UX. Validate all 6 os/arch. |
| GitHub Releases | Yes | Primary | Yes | Signed `func-{os}-{arch}` archives + checksums. |
| npm | Yes | Thin wrapper (see §9) | Yes | Preserve `npm i -g azure-functions-core-tools`. |
| winget | No | New manifest over GH release | Yes | Auto-submit manifest on release. |
| apt / deb | Yes | Thin CLI .deb in MS apt repo | No (post-GA) | Repackage binary only; drop the bundled-runtime model. |
| Homebrew | Yes | Thin formula over GH release | No (post-GA) | Auto-bump formula on release. |
| Chocolatey | Yes | Thin wrapper over GH release | No (post-GA) | Follows once GA channels are stable. |
| MSI | Yes | Not planned for v5 | No | Superseded by winget + installer. Revisit only on demand. |
| Internal tooling feed | Yes | TBD | No | Confirm whether partner tooling still consumes it for v5. |

**GA-blocking minimum:** installer scripts, GitHub Releases, npm, winget.
Everything else (apt, Homebrew, Chocolatey) lands incrementally after GA.

**Open question.** MSI is dropped for v5 in favor of winget plus the
installer. Confirm no enterprise dependency forces us to keep it.

## 9. Decision 6: npm

**Recommendation.** Keep npm as a **thin wrapper** that delivers only the v5
CLI binary, and deliver it **without a `postinstall` script** using the
per-platform package pattern: publish one small package per `{os}-{arch}`
(each carrying just that platform's binary, gated by the `os` and `cpu`
fields), plus a main package that lists them as `optionalDependencies` with a
tiny JS `bin` shim that resolves and execs the right one. npm installs only
the matching platform package and nothing runs at install time. This is the
model esbuild moved to.

During the parallel window:

- npm `latest` stays on **v4**, so existing `npm i -g azure-functions-core-tools`
  installs keep getting v4 and nothing silently jumps a major version.
- v5 publishes under a `next` (and/or `v5`) **dist-tag**. Opt-in installs use
  `...@next`.
- At GA cutover, promote `latest` to v5.

**Why postinstall-free.** The npm and Node ecosystem is moving to restrict or
disable lifecycle scripts by default for supply-chain security: pnpm 10
already blocks dependency build scripts unless they are allowlisted, and npm
has active RFCs in the same direction. A wrapper that downloads its binary in
`postinstall` would silently stop working (or force every user to pass an
allow flag) under those changes, and it already trips security scanners. The
`optionalDependencies` pattern avoids install scripts entirely, so it is both
more secure and more durable.

**Why keep npm at all.** A very large number of CI pipelines install func from
npm. Dropping or renaming the historical package would break them, and a
deprecation-pointer would too, for no real benefit once a clean wrapper exists.

**Open question: package name.** Keep the historical
`azure-functions-core-tools` package, or introduce a new `azure-functions-cli`
package for the "Azure Functions CLI" rebrand?

- Keeping `azure-functions-core-tools` preserves muscle memory and existing CI,
  and the dist-tag plan above manages the v4 to v5 transition on one package.
- A new `azure-functions-cli` package gives a clean, rebranded start (v5 can be
  `latest` immediately, with no dist-tag gymnastics) but loses install-command
  recognition and splits discovery across two names. If we take this route we
  should still publish v5 to the historical name (or leave a deprecation
  pointer there) so existing installs are not stranded.

Leaning toward keeping `azure-functions-core-tools` to avoid fragmentation;
revisit if the rebrand requires a clean package identity.

## 10. Decision 7: Rollback story per channel

**Recommendation.** Roll-forward-first. We do not retag or mutate a shipped
version; a bad release is fixed by shipping the next patch (`-preview.N+1`
during preview). Every channel additionally has a documented
**emergency hatch** for genuine incidents.

| Channel | Normal fix | Emergency hatch |
| --- | --- | --- |
| GitHub Releases | Ship next patch | Mark the bad release as pre-release so the installer skips it; never delete assets users may have pinned. |
| install.sh / ps1 | Resolves next stable automatically | Pin guidance: `VERSION=<last-good>`; mark the bad GH release pre-release. |
| npm | Publish next patch | `npm deprecate` the bad version and move `latest` back to the last good. Never unpublish (npm policy). |
| Homebrew | Bump formula | Revert the formula to the previous revision. |
| winget | New manifest | Submit a manifest revert to the previous version. |
| apt | Publish next patch | Re-point the repo metadata to the previous package. |
| Chocolatey | Push next patch | List the bad version as deprecated; revert the recommended version. |
| Workloads (NuGet) | Publish next patch | **Unlist** (never delete) the bad version so new resolves skip it, while environments that already installed it can still restore. |
| Profiles (CDN) | Publish new profile | Re-point to the previous pinned URL and purge CDN cache (see #5332). |

**Why.** Roll-forward keeps release history immutable and reproducible.
Unlist-not-delete on NuGet is the key rule: it protects any environment that
already installed a now-bad workload version from a broken restore, while
stopping new installs from resolving it.

## 11. Follow-up issues

Implementation items this design surfaces. Items that map to an existing issue
are noted; the rest are new and should be opened after review.

**Release pipeline and coordination**

1. Fully automate the v5 CLI release pipeline: auto-create the GitHub Release,
   upload signed `func-{os}-{arch}` archives + checksums. (Extends
   [#5331](https://github.com/Azure/azure-functions-core-tools/issues/5331).)
2. CLI archive signing + macOS notarization in the release pipeline.
3. Release-gate: every workload release declares and validates its CLI
   compatibility window, including the **minimum CLI version** when it depends on
   a new base/abstractions capability, before publishing. Commit the declaration
   mechanism (NuGet dependency on `Azure.Functions.Cli.Abstractions` vs a
   `func-cli-min` metadata field).
4. Built-in profile publishing: version and update built-in profiles in step
   with host/bundle releases. (Maps to
   [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329) and
   [#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332).)
5. Aggregated release-notes tooling: pull each workload's notes (cross-repo)
   into the CLI release notes.
6. Cross-repo release-readiness checklist and coordinated tagging process.
   (Relates to [#5346](https://github.com/Azure/azure-functions-core-tools/issues/5346).)

**Channels (GA-blocking)**

7. npm v5 thin-wrapper package: deliver the binary via per-platform packages
   (`optionalDependencies` + `os`/`cpu`, no `postinstall`), dist-tag publishing,
   GA `latest` promotion automation, and the package-name decision
   (`azure-functions-core-tools` vs `azure-functions-cli`).
8. winget manifest for v5 with auto-submit on release.
9. Installer coverage validation matrix (6 os/arch) as a GA gate.

**Channels (post-GA)**

10. Homebrew formula/tap for v5 with auto-bump on release.
11. apt/.deb packaging for the v5 thin CLI + Microsoft apt repo publishing.
12. Chocolatey packaging for the v5 thin CLI.

**Policy and comms**

13. v4 maintenance-only policy: public deprecation timeline + backport policy,
    PM sign-off. (Maps to
    [#5359](https://github.com/Azure/azure-functions-core-tools/issues/5359).)
14. Per-channel rollback playbook + tooling (deprecate / unlist /
    mark-prerelease / revert runbooks).

## 12. Summary of Decisions

| # | Decision | Outcome |
| --- | --- | --- |
| 1 | Distribution philosophy | Installer-first. Package managers wrap the CLI binary only; everything else is workloads on NuGet. |
| 2 | v4 + v5 cadence | Independent. No shared release-day or coupled versioning. |
| 3 | v5 release coordination | CLI-anchored single release event. Channels fan out from it. Workloads are pre-published to NuGet. |
| 4 | CLI + workload alignment | No CLI-shipped manifest. Workloads are user-acquired (`func workload install` / `func setup`) and default to **latest compatible** from the catalog. No CLI/workload version gate exists today; we **propose** a per-workload **minimum CLI version** (ideally stamped by the Workload SDK) enforced by the resolver with an upgrade prompt. Environment alignment is via **profiles** (#5332 / #5329). See Open Q #4. |
| 5 | Channel matrix at GA | GA-blocking: installer scripts, GitHub Releases, winget, npm. Post-GA: Chocolatey, brew, apt |
| 6 | npm | Keep, as a thin wrapper delivered via per-platform packages (no `postinstall`). v4 holds `latest` until GA; v5 on a `next` / `v5` dist-tag. |
| 7 | Rollback | Roll-forward-first, never retag. Workloads are unlisted, never deleted. |
| 8 | v4 posture | Maintenance-only (security + critical bugs). Features are v5-only. |

> Open Q regarding #4: Do we need to have a manifest or a min CLI version declared for workloads?
> Open Q regarding #5: Do we want to support the same channels we did for v4?
> Open Q regarding #6: Keep `azure-functions-core-tools` vs a new `azure-functions-cli` package?

## 13. Open questions

- Exact v5 cadence (monthly minor train?) and who owns the release calendar.
- Confirm MSI can be dropped for v5 (no enterprise blocker).
- Workload minimum-CLI declaration: NuGet dependency on
  `Azure.Functions.Cli.Abstractions` vs a `func-cli-min` metadata field, plus the
  resolver message when a newer workload needs a newer CLI (carried from
  workload-package-layout §6/§11).
- Confirm whether partner tooling still needs the internal tooling feed for v5.
- npm package name: keep `azure-functions-core-tools`, or introduce a new
  `azure-functions-cli` package for the rebrand (affects the dist-tag plan).
- Track the npm/Node move to restrict lifecycle (`postinstall`) scripts and
  keep the npm delivery postinstall-free (see §9).

## 14. References

- Issue: [#5328 Design: v4 + v5 release story](https://github.com/Azure/azure-functions-core-tools/issues/5328)
- `vnext-release-process.md` (per-component tag and pipeline mechanics)
- `v5-ga-plan.md` (GA milestones and release-process notes)