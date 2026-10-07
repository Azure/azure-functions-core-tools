# CLI v5: Open Decisions

**Status:** PROPOSAL - FOR TEAM REVIEW. All eleven decisions below remain OPEN.

[Entry guide](../v5-ga-readiness.md) | [Roadmap and issue input](roadmap.md)

Use this page to agree release scope and acceptance. Issue-specific input stays
in the roadmap. When closing a decision, record the choice, approver, rationale,
accepted risks and follow-up. References identify related work; the roadmap's
inventory caveat applies.

## D01

**Decision**

Which features are required for the release, and which can move later?

**Options**

Keep the full intended program or select a smaller supported feature set.
In-place update, telemetry export and template migration are proposed work; none
is included or deferred until the team decides.

**Decision needed**

Agree the feature list and approve cuts individually, using dependency and
adoption evidence before dependent delivery. Cuts must not weaken the
[protected release requirements](../v5-ga-readiness.md#protected-release-requirements).

## D02

**Decision**

Complete template migration or keep the existing providers?

**Options**

For [#5655](https://github.com/Azure/azure-functions-core-tools/issues/5655), choose
full migration or a smaller migration that completes each selected supported path.
Alternatively, retain tested existing-provider scaffolding without promising the
new package lifecycle, template-first init or discovery experience.

**Decision needed**

Agree the supported path with template, product and parity reviewers before
placement or cut approval. Internal eligibility is not a complete user experience;
required acquisition, adapters, context, selection and invocation cannot be cut
as optional ecosystem work.

## D03

**Decision**

What minimum workload compatibility checks are required?

**Options**

Use checks for agreed install/load combinations and known incompatibilities.
Add broader metadata/evaluator work from
[#5673](https://github.com/Azure/azure-functions-core-tools/pull/5673) only when the
contract needs it; template eligibility is a separate concern.

**Decision needed**

Agree supported combinations, rejection behavior and publisher obligations with
workload, publisher, security and release reviewers before dependent code. Preserve
exact RID/version/source selection and actionable errors.

## D04

**Decision**

How should broader Preview 3 regression coverage be tracked?

**Options**

Use a focused child of [#5353](https://github.com/Azure/azure-functions-core-tools/issues/5353)
or a separate linked tracker with an explicit owner.

**Decision needed**

Agree the tracking boundary and owner without silently expanding
[#5656](https://github.com/Azure/azure-functions-core-tools/issues/5656) beyond its
representative CLI-functional scenarios. Pin fixtures and reuse existing process
isolation; a generalized framework is not required. Tracking coordination should
not block baseline/scenario preparation.

## D05

**Decision**

Which packaging path should winget use, and is MSI required?

**Options**

An eligible archive, MSI or another accepted packaging path for
[#5419](https://github.com/Azure/azure-functions-core-tools/issues/5419).

**Decision needed**

Obtain channel/enterprise acceptance requirements early, then select and qualify
the Windows packaging path. MSI is not an independent channel commitment; a
scope vote or install-method detection does not establish whether it is required.

## D06

**Decision**

Which workflows, migration alternatives and v4 support details must close?

**Options**

For [#5355](https://github.com/Azure/azure-functions-core-tools/issues/5355), retain
required workflows or accept named gaps with tested alternatives and guidance.

**Decision needed**

Agree parity, migration paths and EOL/security-support details separately with
language/tooling, product, partner and support reviewers before scope closure.
Assess Durable CLI operations and packaging before cuts; incomplete CLI parity
does not mean all Durable applications cannot run. Independent release cadence
and v4 maintenance at v5 GA remain existing policy.

## D07

**Decision**

Who accepts each outcome, and what delivery/review capacity is available?

**Options**

Staff delivery and review in parallel or reduce optional features to fit capacity.

**Decision needed**

Confirm accountable owners, reviewers, external contacts and acceptance
responsibilities. Assignment is not a staffing estimate or delivery commitment;
use actual availability, not inferred hierarchy. Start channel, signing/security,
partner and migration preparation before feature completion.

## D08

**Decision**

What telemetry and diagnostics should ship?

**Options**

Choose reviewed minimal export, broader accepted collection or verified no-export
behavior.

**Decision needed**

Agree telemetry content, opt-out, privacy and lifecycle acceptance before
inclusion, with telemetry and privacy/security reviewers. Preserve supported
diagnostics for
[#5349](https://github.com/Azure/azure-functions-core-tools/issues/5349) and
[#5522](https://github.com/Azure/azure-functions-core-tools/issues/5522).
Technical PR validation is not privacy-policy approval.

## D09

**Decision**

Which platform/stack/channel combinations and evidence qualify the release?

**Context**

Changing advertised coverage requires product/partner agreement; untested targets
cannot count as passing.

**Decision needed**

Define the supported cells for [#5421](https://github.com/Azure/azure-functions-core-tools/issues/5421)
and artifact/payload checks before qualification. Agree measured latency limits
with platform, quality, signing and release owners. Bind final version, signing,
packages and wrapper results to the release record.

## D10

**Decision**

What localization policy is supported at GA?

**Options**

Retain translations or defer that breadth with an explicit supported language
policy for [#5361](https://github.com/Azure/azure-functions-core-tools/issues/5361).

**Decision needed**

Agree the policy with product, localization and support before scope freeze;
reclassify implementation if required for release. Accessibility remains required
independently.

## D11

**Decision**

Which Kubernetes/KEDA CLI workflows should be supported?

**Options**

Use usage/parity evidence for [#5345](https://github.com/Azure/azure-functions-core-tools/issues/5345)
to choose implementation, later delivery with supported alternatives, or deliberate
exclusion with rationale.

**Decision needed**

Agree the supported workflows and alternatives with tooling, product and affected
partners. An active decision issue does not automatically make new implementation
mandatory.

## Candidates for Post-GA

These are conditional proposals, not approved cuts or issue placements. A linked
issue may contain both optional breadth and a required prerequisite. Preserve
the required portion and obtain team approval before moving the rest.

| Area / related issues | Proposed cut and retained requirement | Risk / input before approval |
| --- | --- | --- |
| Durable CLI: #5340, #5341 | Later workload/CI breadth; retain supported runtime behavior and alternatives | Assess named CLI workflows and provide migration/v4 support; adoption impact may be material |
| Template ecosystem: #5629, #5631, #5632, #5636, #5651, #5653 | Optional management/search/sample breadth only; retain the complete chosen scaffolding path | D02 must identify required acquisition/adapters/invocation inside these issues |
| Quickstart extraction: #5324, #5325, #5640 | Keep the qualified current flow while relocating architecture later | Preserve pinned manifest/content distribution and test cache/network failures |
| Doctor: #5347 | Later diagnostic automation; retain working logs/help and troubleshooting | Accept the support effort of manual diagnostics |
| Aspire: #5380 | Later added integration; retain supported run/start and partner paths | Confirm existing partner promises before omitting the integration |
| Schema store: #5348, #4936 | Later editor-store registration, not removal of required schemas | Validate advertised public schema URLs, definitions and examples |
| Settings: #5343 | Later new settings surface; retain current configuration compatibility | Audit supported host/local settings behavior and document differences |
| Meta/setup: #5383, #5384 | Later internal refactor breadth; retain acquisition/profile/setup correctness | A required correctness fix cannot be deferred merely because it is in a refactor |
| Pack integration: #5342 | Later redesign only if a supported packaging/deployment path exists | Test alternatives and migration before approving a functional cut |
| Owner-repository migration: #5346 | Move topology later; retain named current publishers and release responsibility | No publisher gap; preserve package contracts and repeatable delivery |
| Kubernetes/KEDA: #5345 | Implementation depends on D11; retain accepted tooling alternatives | Assess usage/adoption and provide explicit migration/support guidance |
| Localization: #5361 | Translation breadth depends on D10; retain the supported language policy | Do not imply unavailable translations; preserve accessibility |
