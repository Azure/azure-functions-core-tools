# Azure Functions CLI v5: Pre-Ignite GA Review

**Status:** PROPOSAL - FOR TEAM REVIEW.  
**Objective:** GA before Ignite 2026.  
**Evidence snapshot:** 2026-10-06. Exact freeze and release dates are unapproved planning hypotheses.

## Purpose

Give Francisco, Fabio, Sarah, and the CLI team a requirements-first decision
surface for a defensible pre-Ignite GA. This package extends the
[readiness guide](../v5-ga-readiness.md), not a second independent roadmap.
Classifications, checkpoint placement, and feature cuts are proposals for review,
not approvals or evidence that a release gate has passed.

## Planning Principles

- Requirements before issues; map tracking to required outcomes afterward.
- Feature completeness is not GA readiness.
- Protect quality, security, required distribution, and release safety.
- Move feature breadth first when the schedule is under pressure.
- RC1 should be capable of shipping as GA, with no planned major feature tranche afterward.
- RC2 exists only when blocking fixes require another candidate.

## Checkpoints

| Checkpoint | Purpose |
| --- | --- |
| Preview 3 | Shipped behavioral baseline feeding later comparisons, not new feature work. |
| Preview 4 | Reliability, plumbing, packaging, and candidate quality. |
| Preview 5 | Complete the selected GA feature tranche and strengthen scenario coverage. |
| RC1 | A candidate that could actually ship as GA. |
| RC2, if required | Blocking fixes only, with regression and channel revalidation. |
| GA | Publish and cut over the already validated release. |

Use the [meeting roadmap](roadmap.md) for the five readiness lanes. The original
[GA inventory](../v5-ga-plan.md), [release design](../cli-release-story.md), and
[component release process](../vnext-release-process.md) remain supporting sources.

## Review Pages

| Page | Comment on |
| --- | --- |
| [Roadmap](roadmap.md) | Checkpoint purpose and lane-specific exit outcomes. |
| [Work items](work-items.md) | Required capabilities, classifications, predecessors, gates, tracking/owner gaps, and delegation. |
| [Decisions and proposed cuts](decisions.md) | Individual options, recommendations, cut risks, and approval/input required. |
| [Documentation impact](documentation-impact.md) | Current/stale/historical guidance and the protected P0 documentation follow-up. |

## Review Focus

**Largest decisions:** retained GA features and explicit cuts in one scope review;
templating migration versus supported
fallback; minimum workload compatibility; differential-quality ownership;
MSI/winget acceptance; Durable/parity and v4 support; committed owners and capacity.

Full in-place update and enabled telemetry export are proposed targets, not
automatically minimum GA features. If either is omitted, the team still needs
tested manager/installer update and recovery, usable diagnostics, and privacy
acceptance for everything shipped. Retained targets must finish in Preview 5;
RC1 qualifies them rather than continuing feature implementation.

**Calendar risks, not a proven critical path:** channel submission and validation,
signing/notarization, security review, partner response, and parity/migration work
can consume elapsed time independently of feature implementation. Owner dates and
delivery evidence are needed before claiming the target is feasible. These
activities start in parallel before Preview 4 exits; their RC1 completion gate is
not a suggested start date.

**UNTRACKED REQUIRED WORK / TRACKING DECISION REQUIRED:** an explicit pinned
Preview-3-to-candidate semantic differential and a candidate-readiness record
covering source, artifacts, workloads/profiles, and gate results. Existing E2E and
release issues are possible parents; the inspected tracking does not adequately
specify these outcomes. This is not a claim that no other tracker exists.

**Proposed cuts:** diagnostic/editor conveniences, internal refactors, workload
repository migration, and optional template breadth outside the retained
acquisition/migration path. Durable-specific CLI
parity and templating fallback need explicit product/migration decisions, not
silent omissions. Required safety work is not in the cut list.

Qualification records must bind the final GA version, signed artifacts and
channel packaging. Rebuilding or re-signing an RC does not preserve its tested
byte identity; validate replacement artifacts before publication.

## Explicit Non-Decisions

This review does not approve exact dates, feature cuts, owner assignments,
candidate inclusion, channel changes, releases, or a branch transition. A green
PR, an assigned issue, or accessible package metadata is not release certification.
The earlier December planning horizon remains historical provenance, not the
current target; useful architecture and dependency information is retained.

## Evidence Boundary

Product source/Git, repository docs, reconciled issue/PR state, distribution
metadata, and established CLI planning inform this proposal. Workflow operational
or dogfood records are not product/release evidence. No private investigation
package, operational logs, customer material, or conversation transcript is copied
here. Mutable facts are dated observations and must be rechecked when used to
approve an actual candidate.
