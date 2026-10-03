# Azure Functions CLI v5: GA Readiness

This document describes **how we decide a v5 build is ready to ship**, and what
must be true before GA. It complements, and does not replace, the existing
planning documents:

- [`v5-ga-plan.md`](./v5-ga-plan.md) — the milestone-organised work breakdown.
- [`vnext-release-process.md`](./vnext-release-process.md) — how a component is
  tagged and released.
- [`cli-release-story.md`](./cli-release-story.md) — distribution, parallel
  cadence, channel matrix, and rollback decisions.

Read those for *what work exists* and *how we release*. Read this for *what
"ready" means* and *which checkpoint each class of work must land by*.

**This document deliberately contains no calendar dates.** Progression is
described by quality and readiness boundaries. Scheduling lives in release
tracking and team discussion, where it can change without rewriting the
architecture of the plan.

---

## 1. Feature complete is not GA ready

A release is evaluated on four independent lanes. All four must reach the
required state before a build can be called a GA candidate.

| Lane | Question it answers |
| --- | --- |
| **Feature** | Does the CLI do what v5 promised? |
| **Quality** | Do we have evidence it still works, and that nothing regressed? |
| **Distribution** | Can users actually get it, on every supported channel and platform? |
| **Productization** | Is it supportable — documented, signed, migratable, with partners informed? |

These lanes move at different speeds and have different owners. A build can be
feature complete and nowhere near GA ready, because distribution channels are
unbuilt or migration guidance does not exist.

The most common failure mode for a release like this one is to treat the feature
lane as the schedule and discover the other three at the end. The checkpoint
model below exists to prevent that.

### When scope must be reduced

Schedules compress. When they do, the order of reduction is not a judgement call:

> **Cut feature scope before lowering quality, security, or release safety.**

Feature scope is recoverable — a feature that misses GA ships in the next minor
release, and the only cost is that users wait. The other three lanes are not
recoverable in the same way. A channel that ships unsigned, a release with no
rollback path, or a GA with no regression coverage creates obligations that
outlive the release and are far more expensive to repair afterwards than to do
once, on time.

This means some work is explicitly **not available** as schedule relief:

- Quality gates required for a checkpoint.
- Signing, notarization and supply-chain requirements.
- Distribution channels that have been decided as GA-blocking.
- Migration guidance and the parity position.
- Release and rollback mechanics.
- Partner readiness for tools that embed the CLI.

Deferring a feature requires a product decision and clear communication,
particularly where it leaves a user segment unable to adopt v5. Deferring an
item from the list above requires a decision to change what GA means.

---

## 2. Checkpoints

Each checkpoint is a **quality boundary**, not a date. A build does not advance
until its gates are satisfied.

| Checkpoint | Product objective | Feature focus | Quality gate | Distribution / productization gate |
| --- | --- | --- | --- | --- |
| **Preview 3** | Behavioural baseline | Shipped | Treated as the reference build that later candidates are compared against | CDN distribution with version-pinned artifacts and published checksums |
| **Preview 4** | Reliability and plumbing | Hardening of existing surfaces; install and packaging correctness; no major new feature surface expected | Baseline differential established; bug bash on changed surfaces; unit and component suites green | Install path validated across supported platforms; release notes current for the build |
| **Preview 5** | Feature-completeness push | Remaining planned feature scope lands — self-update, template constraints, release mechanics | Automated CLI scenario suite running against the baseline; no unexplained regressions | Additional release channels exercised; release mechanics rehearsed rather than improvised |
| **RC1** | Plausible GA candidate | No planned major feature tranche remains | Full scenario suite green across the supported matrix; release and channel validation green | All GA-blocking channels operational; signing and verification in place; migration and parity positions closed sufficiently to ship |
| **RC2** | Only if required | Blocking fixes only | Re-validation of the affected areas plus full regression | Re-validation of affected channels |
| **GA** | Final validated release | Scope frozen | All release-blocking suites green; accepted known issues documented | All channels published and verified; cutover completed |

**RC2 is conditional.** It exists only if RC1 surfaces defects that block
release and require another candidate. It is not planned, and planning work into
it would defeat the purpose of RC1.

Checkpoint contents are **candidate scope**, not commitments. Feature placement
may move between previews; the quality bar for a checkpoint does not move to
accommodate a feature.

---

## 3. Feature lane

Major workstreams, described at the level of the outcome required before GA.
Issue-level tracking lives in [`v5-ga-plan.md`](./v5-ga-plan.md) and GitHub.

| Workstream | Outcome required before GA |
| --- | --- |
| **`func update`** | The CLI can update itself safely: integrity-verified downloads, serialised concurrent updates, rollback on failure, and correct behaviour when the CLI was installed through a package manager that owns updates. See [`func-update.spec.md`](./func-update.spec.md). |
| **Profiles and release mechanics** | Profiles are versioned and released through a defined process rather than by hand, with a documented path for correcting a bad profile release. |
| **Templating** | `func init` and `func new` run on the func-owned template catalog, templates can declare what they require, and a template that cannot run is shown as unavailable with an actionable reason rather than silently failing. |
| **Telemetry and diagnostics** | Per-command instrumentation with a settled export lifecycle, honouring opt-out, with no measurable cost to command startup. |
| **Quickstart and workload evolution** | Quickstart content and its distribution follow the workload and template-package model rather than remaining special-cased in the CLI. |
| **Quality automation** | See the quality lane. This is a feature of the project, not a side activity. |
| **Performance** | Command startup and common-path latency are measured, tracked across builds, and do not regress silently. |
| **Parity** | A per-language and per-tooling position against v4 — either at parity, deliberately changed with migration guidance, or deliberately dropped with a documented rationale. |

Nothing here is promised into a specific preview. The constraint that matters is
that each must reach its GA outcome **by RC1**, because RC1 is defined as having
no planned major feature tranche after it.

---

## 4. Quality lane

**Quality is a P0 workstream for v5**, not a phase at the end.

### Baseline-and-differential model

Preview 3 is the **pinned behavioural baseline**. Every later candidate runs the
same scenarios, and results are compared against that baseline. The installer
supports pinning an explicit version, and published artifacts carry checksum
sidecars, so a baseline run is reproducible and verifiable regardless of which
version the release manifest currently advertises.

A comparison produces one of:

- **Unchanged pass** — behaviour matched the baseline.
- **Expected feature delta** — behaviour changed, and the change was declared.
- **Regression** — behaviour changed with no declared expectation. Blocking.
- **New capability** — a scenario the baseline could not run.
- **Baseline limitation** — the feature did not exist in the baseline.
- **Inconclusive** — infrastructure failure or flake. Investigated, never
  auto-passed.

**A difference is never classified as a regression merely because bytes
changed.** Only stable-contract observables gate a release: exit codes, required
file presence, semantic configuration values, structured output payloads,
command outcome, application startup and invocation, error behaviour, cleanup,
and the absence of unexpected error output on success paths. Human-readable text
and generated file contents are reported, not enforced — templates and wording
evolve independently and legitimately.

Intentional changes are declared **in the pull request that causes them**, next
to the scenario they affect, so the burden sits with the author who has the
context.

### Local-first execution

Scenarios run without cloud resources wherever possible. The CLI manages its own
storage emulator, so project creation, host startup and trigger invocation are
all locally verifiable. Scenarios that genuinely require cloud resources are
isolated and kept out of the fast suites.

### Test maturity

Automation grows with the release, rather than arriving at the end:

| Stage | Scope |
| --- | --- |
| In-process unit and component tests | Already the pull-request gate |
| CLI smoke scenarios | Real binary, fast, no network |
| CLI behavioural scenarios | Single-command behaviour including negative and error paths |
| CLI end-to-end scenarios | Cross-command journeys with the managed emulator |
| Release and channel validation | Install, upgrade, integrity verification, per-channel publication |

Expensive and externally dependent suites are **not** pull-request blocking.
Making them so is the fastest way to have the whole suite disabled.

### Representative inputs over production coupling

The CLI's template and workload behaviour is validated using **fixture packages
authored for testing**, following the pattern already used for workload tests.
Validating that a production template scaffolds correctly belongs to the
repository that owns that template. Coupling CLI release gates to every
independently evolving template file makes the CLI's quality signal hostage to
unrelated changes.

---

## 5. Distribution lane

Channel support follows the matrix decided in
[`cli-release-story.md`](./cli-release-story.md). Summarised here because it is a
GA gate, not an implementation detail.

| Channel | Status for v5 |
| --- | --- |
| Install scripts (`install.sh` / `install.ps1`) over CDN | GA-blocking. Must be validated on every supported OS and architecture. |
| Homebrew | GA-blocking. |
| winget | GA-blocking. |
| npm | GA-blocking. |
| APT | Post-GA. |

Chocolatey, MSI, Scoop, RPM and the internal tooling feed were **removed** from
the v5 distribution plan. They should not be reintroduced as GA channels without
a new decision.

**Install-method detection is separate from channel support.** The CLI
recognises installations it does not own — including package managers that are
not v5 release channels — so that self-update can decline cleanly and point the
user at the mechanism that actually owns the installation. Detecting a package
manager is not a commitment to release through it.

### Integrity and signing

Published artifacts are signed, and release staging invokes platform-appropriate
signing for Linux, macOS and Windows. Before GA the remaining requirements are
that signing is applied to every artifact a user can download on every supported
platform, that macOS notarization requirements are satisfied, and that signature
verification is documented for users who want to check an artifact themselves.

### Rollback

The release posture is **roll-forward-first**: a shipped version is not retagged
or mutated, and a bad release is corrected by shipping the next patch. Each
channel has a documented emergency hatch, and the governing rule for workload
packages is **unlist, never delete** — so environments that already resolved a
bad version are not broken while new installs stop selecting it. The per-channel
detail lives in [`cli-release-story.md`](./cli-release-story.md).

What remains before GA is operational rather than architectural: the per-channel
procedures must be written down and rehearsed, not merely decided.

---

## 6. Productization lane

Work that does not change CLI behaviour but without which v5 cannot be
supported. Most of this originates in [`v5-ga-plan.md`](./v5-ga-plan.md).

| Area | Required before GA |
| --- | --- |
| **Parity position** | A per-language and per-tooling statement of v4 parity: at parity, changed with guidance, or dropped with rationale. |
| **Migration and v4 deprecation** | Migration guide, v4-to-v5 command map, behaviour-difference catalogue, known-gaps page, configuration-schema compatibility audit, and a deprecation position for v4. |
| **Security and supply chain** | Token-handling review, workload package signature verification, feed pinning, supply-chain audit, threat model. |
| **Documentation** | External documentation refreshed for v5, in-repo documentation rewritten, and user-facing strings carrying the current product name consistently. |
| **Partner readiness** | Teams that embed the CLI — editor extensions, Azure CLI, JetBrains tooling — informed early enough to raise concerns before GA, with feedback triaged. |
| **Release playbook** | Per-channel release and rollback procedures, cross-repo release readiness, coordinated versioning across components and channels, and a release-day checklist. |
| **GA cutover** | Default-branch transition, the `v5.0.0` tag, release notes covering the full delta from v4, refreshed issue templates, and a documented triage and support posture. |
| **Accessibility** | Colour-disabled and non-interactive terminal behaviour audited; interaction surfaces usable with assistive technology. |
| **Localization** | A decision on whether localization is in scope for GA, then execution if it is. |

Two items on this list have no engineering dependency but long lead times —
**partner readiness** and **migration guidance**. They are the most likely to
become the critical path if they start late, precisely because nothing blocks
them.

---

## 7. What RC1 means

RC1 is the first build that could ship as GA if nothing further were found. That
is a strong claim, and it is the point of having an RC at all.

A build is RC1 when:

1. Planned major feature scope is complete — **no further feature tranche is
   planned after RC1.**
2. Required quality gates are green: the scenario suite passes across the
   supported matrix, and every differential against the baseline is either an
   unchanged pass or a declared, reviewed expected delta.
3. All GA-blocking distribution channels are operational, not merely designed.
4. Signing and integrity verification are in place for every published artifact.
5. Parity and migration positions are closed sufficiently to ship — not
   necessarily "at full parity", but with a documented, defensible position.
6. Partner teams have been informed and their blocking concerns resolved.
7. Release and rollback mechanics are executable by someone following the
   playbook, rather than reconstructed from memory.

If a feature slips past RC1, it moves to a post-GA release. It does not convert
RC1 into another preview — doing so means the project has no release candidate,
only previews with optimistic names.

---

## 8. Validation progression

Each preview feeds the next release:

```
preview build
  → automated scenario suite + bug bash
    → defects filed with reproduction detail
      → fixes
        → permanent regression scenarios added to the suite
          → next checkpoint inherits stronger gates
```

Toward the end of the cycle this converges on RC1, where the suite is the
primary signal and the bug bash is confirmation rather than discovery.

**Bug bashes complement automated validation; they do not substitute for it.** A
bug bash finds what nobody thought to automate. Automation ensures that what was
found once never returns silently. A finding is only fully resolved when a
scenario exists that would have caught it.

Bug-bash reports should carry enough detail to convert directly into a scenario:
the exact command, platform and architecture, how the CLI was installed, the
environment overrides in effect, whether the terminal was interactive, the full
output, and the exit code.

---

## 9. Relationship to the original plan

This document extends [`v5-ga-plan.md`](./v5-ga-plan.md) rather than replacing
it. The milestone breakdown there remains the work inventory. Several directions
have since evolved, and the evolution is intentional:

| Original direction | Current direction |
| --- | --- |
| Remove runtime-identifier suffixes from workload packages | Workload packages are runtime-identifier aware, with pointer manifests mapping an identifier to its platform-specific implementation |
| Base CLI and workload version compatibility expressed as a matrix | Expressed as versioned contracts with explicit metadata and a shared evaluator; the enforcement scope for GA is a decision in its own right |
| Broad distribution channel list | Narrowed to a decided GA-blocking set, with the remainder removed or moved post-GA |
| Quality captured as "E2E testing" plus a performance budget | Expanded into an explicit baseline-and-differential strategy with staged test maturity, described in the quality lane above |

The original plan's scope for parity, migration, security, documentation,
partner readiness and GA cutover is **carried forward unchanged**. It remains
GA-gating, and the productization lane exists to keep it visible alongside
feature work rather than behind it.
