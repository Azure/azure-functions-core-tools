# Documentation Impact And Follow-Up

**Status:** PROPOSAL - FOR TEAM REVIEW.  
**Evidence snapshot:** completed CLI reconciliation, 2026-10-06.

[Executive review](README.md) | [Roadmap](roadmap.md) | [Work items](work-items.md) | [Decisions](decisions.md)

## P0 Follow-Up

**Review and update all relevant Core Tools `docs/` documentation as part of CLI
v5 pre-Ignite GA readiness. Some existing design/roadmap/release documents are
stale. Preserve historical provenance where useful, but update current
authoritative documentation to reflect the agreed architecture, release
checkpoints, scope, dependencies, and pre-Ignite 2026 GA target.**

This is WI-30. Inventory and independently verifiable source corrections can
start now; scope-dependent updates follow the relevant Fabio/Sarah/team choices,
not every open decision. The review includes top-level `proposed/` references
consumed by developer docs. Historical design records need correct status and
successor links, not automatic rewrites as current GA guidance. This package
does not implement the broad cleanup.

## Classifications

- `CURRENT`: useful current contract/process or explicitly proposed design, not proof that implementation or release acceptance is complete.
- `STALE CURRENT GUIDANCE`: active guidance includes claims contradicted by current source or distribution.
- `HISTORICAL / PRESERVE`: retain provenance/dependencies; separate old planning from the controlling target.
- `SUPERSEDED`: the named direction/section no longer describes the implementation; retain history with an explicit current reference.
- `MISSING`: a required outcome document was not established in the inspected scope; not an exhaustive repository-wide absence claim.
- `UNKNOWN`: current alignment or completion has not been verified; do not invent a rewrite.

Every classification applies to the scope stated in the first column. A useful
design can contain a superseded section, and a correct contract can have a stale
implementation-status banner. Remediation must distinguish those cases.

## Existing Documents

| Document / scope | Purpose | Classification | Required update | Evidence for update | Decision dependency | Target checkpoint | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- |
| [proposed/v5-ga-readiness.md](../v5-ga-readiness.md) | Proposed readiness model | CURRENT | Align intended feature outcomes with reviewed cutline; keep proposal status until accepted | This package separates required outcomes from optional migration/extraction; exact dates remain unapproved | Relevant scope/acceptance choices | Review now; agreed guidance before RC1 | P0 |
| [proposed/v5-ga-plan.md](../v5-ga-plan.md), original milestone inventory | Architecture/scope/dependency provenance | HISTORICAL / PRESERVE | Retain inventory; annotate agreed current requirements/checkpoint mapping rather than silently redistribute issues | Original/current inventory contains broad feature and backlog scope; later decisions narrow channels and change RID direction | D01, D11, D12 | Preview 5 scope; RC1 authoritative map | P0 |
| [proposed/cli-release-story.md](../cli-release-story.md), active release guidance | Channel, cadence, rollback design | STALE CURRENT GUIDANCE | Preserve decided channel/cadence policy; replace GitHub-based installer/background claims and CDN emergency-hatch mechanics with current contracts | Current installers resolve CDN version.json; GitHub prerelease flags alone do not change that manifest; MSI remains an open question | D05, D10; operator input | Preview 5 rehearsal; RC1 playbook | P0 |
| [proposed/vnext-release-process.md](../vnext-release-process.md) | Draft component tag/version/publish process | CURRENT | Confirm operator acceptance and exact final-version/signing/payload qualification before promotion | Draft requires tag/props agreement and suffix-free GA; prior RC results do not certify rebuilt GA bytes | D03, D08, D10 | Preview 5 rehearsal; RC1/final artifact qualification | P0 |
| [proposed/func-update.spec.md](../func-update.spec.md), artifact/detection examples | Self-update contract | STALE CURRENT GUIDANCE | Keep update integrity/ownership goals; align archive names/types and npm/install-method examples with retained implementation/channel decisions | Current artifacts use func-<rid>.zip or .tar.gz; older spec examples use Azure.Functions.Cli.<rid>.<version>.zip and legacy npm naming | D01, D05, D10 | Before update qualification; RC1 | P0 |
| [proposed/workload-package-layout.md](../workload-package-layout.md), portable-only restriction | Package layout design history | SUPERSEDED | Mark portable-only/no-RID sections historical; link the implemented pointer/implementation and runtimeIdentifier contract | Current RID-aware packages, exact pointer/implementation version and feed behavior, and tools/<rid> guidance contradict portable-only restriction | D03 | Before publisher/compatibility qualification | P0 |
| [docs/cli-architecture.md](../../docs/cli-architecture.md) | Runtime/DI/console/home architecture | STALE CURRENT GUIDANCE | Refresh acquisition, run/start, version-discovery and telemetry lifecycle status; retain correct home/console contracts | Old intro says feed acquisition follows later; flow/version check is GitHub-oriented; prototype/stub and bounded StopAsync flush descriptions do not reflect complete current lifecycle | D01, D02, D03, D09 | Preview 5; authoritative before RC1 | P0 |
| [docs/repo-structure.md](../../docs/repo-structure.md) | Repository, build and CI map | STALE CURRENT GUIDANCE | Refresh implemented acquisition/projects/SDK references and scoped validation instructions | Old acquisition-follow-up text and SDK/status examples need reconciliation with current tree | D01, D03 | Before RC1 | P0 |
| [docs/building-a-workload.md](../../docs/building-a-workload.md) | Workload authoring and RID pointer guidance | CURRENT | Preserve useful current RID guidance; repair relative spec links and align affected examples with the accepted package contract | Authoring guide has pointer/exact-version/same-feed/runtimeIdentifier guidance; ./proposed/... link resolves under docs/, while actual proposals are top-level | D03 | Before publisher guidance/RC1 | P0 |
| [docs/func-start-output-modes.md](../../docs/func-start-output-modes.md), prototype status | Compact/plain/JSON output contract | STALE CURRENT GUIDANCE | Update synthetic-only status; describe current real-host behavior and demo opt-in without erasing intended output semantics | StartCommand has real-host launch and hidden demo option; factory registers host process services | D01, D10 | Preview validation; before RC1 | P0 |
| [docs/func-start-json-schema.md](../../docs/func-start-json-schema.md), prototype status | NDJSON record contract | STALE CURRENT GUIDANCE | Verify schema against real host/initialization output; retain versioning and stable record semantics; correct prototype-only banner | Current real-host execution contradicts synthetic-only status; schema agreement still requires behavioral tests | D04, D10 | Preview 5; RC1 contract validation | P0 |
| [docs/template-engine-integration/design.md](../../docs/template-engine-integration/design.md) | Proposed session/list/resolve/invoke and eligibility contract | CURRENT | Clearly distinguish retained design, implemented internal catalog, and production command migration; update only after the chosen scope | Same-session context, group resolution, invocation, diagnostics and force policy are design requirements, not proof of current command consumption | D02, D03 | Preview 5 scope; before RC1 | P0 |
| [docs/template-package-install/design.md](../../docs/template-package-install/design.md) | Template package lifecycle design | CURRENT | Separate prerequisites of retained migration from optional lifecycle breadth; preserve design dependencies | A proposed ecosystem cut cannot waive required acquisition/adapters or prove completed implementation | D02, D01 | Preview 5 scope; RC1 guidance | P0 |
| [docs/azure-samples-template-pipeline/design.md](../../docs/azure-samples-template-pipeline/design.md) | Proposed sample supply pipeline contract | CURRENT | Preserve proposed design status; annotate agreed release scope separately from implementation status | Design specifies a separate control plane, goals and non-goals; possible deferral is not evidence that the design is historical or superseded | D02, D01 | Preview 5 scope; before RC1 references | P0 |
| [docs/func-init-execution/design.md](../../docs/func-init-execution/design.md) | Init execution design | UNKNOWN | Validate against the agreed migration/fallback and current init implementation; do not assert all design text is stale | Current retained-path/migration distinction is established; complete document-by-document alignment was not part of reconciliation | D02, D04 | Before RC1 | P0 |
| [docs/func-init-quickstarts/design.md](../../docs/func-init-quickstarts/design.md) | Init/quickstart integration design | UNKNOWN | Review manifest/content/contract boundaries against retained path and cutline | Quickstart extraction is proposed, not approved; current path and design completion are distinct | D02, D01 | Before RC1 | P0 |
| [docs/func-new-execution/design.md](../../docs/func-new-execution/design.md) | New-command execution design | UNKNOWN | Reconcile command surface and migration prerequisites after D02 | Current production provider path is known; full proposed-surface implementation is not certified | D02, D04 | Before RC1 | P0 |
| [docs/templating-system/design.md](../../docs/templating-system/design.md) | Broader templating architecture | UNKNOWN | Trace retained/deferred dependencies and agreement with integration/package designs after cutline | Related design surfaces exist; active issues alone do not determine required GA breadth | D02, D03, D01 | Before RC1 | P0 |
| [README.md](../../README.md), installation guidance | Contributor/user entry point | STALE CURRENT GUIDANCE | Correct GitHub-download description and link the agreed v5 install/migration guidance without rewriting unrelated content | Reconciled README claims GitHub download while installer source and metadata use CDN | D01, D06, D10 | Candidate instructions; before RC1 | P0 |

## Outcome Documents Not Established

`MISSING` here describes a required outcome not established by the inspected
documentation/tracking, not a guessed path to create or proof that no private
operator document exists. Agree owner/location before authoring it.

| Required document / purpose | Classification | Required update | Evidence for update | Decision dependency | Target checkpoint | Priority |
| --- | --- | --- | --- | --- | --- | --- |
| Approved pre-Ignite scope and outcome-owner map | MISSING | Record accepted scope and accountable outcomes; use capacity/dates as planning inputs | This package is a proposal; assignments do not establish capacity or an approved feature cutline | D01, D08 | Early long-lead allocation; before dependent delivery | P0 |
| Supported installer/distribution/RID acceptance matrix | MISSING | Specify supported distributions, architectures, channel cases, and evidence required | #5421 is tracked but underspecified; original inventory names six RID targets | D05, D10 | Preview coverage now; complete RC1 | P0 |
| Cross-release semantic differential/result contract | MISSING | Define explicit tracker, owner, scenario identities, expected-delta handling and reproducible results | #5656 scaffolding and #5353 E2E scopes do not adequately specify pinned baseline/candidate comparison | D04 | First Preview 4 comparisons; automated Preview 5 | P0 |
| Per-candidate source/artifact/workload/profile/gate record | MISSING | Open selected input identities before testing; close with qualification results; requalify final version/signing/packaging changes | #5440 archive-before-manifest acceptance is only part of closure; suffix-free GA artifacts may differ from RC builds | D03, D04, D10 | Before tests and before promotion, every candidate from Preview 4 | P0 |
| Executable CDN-aware release/rollback playbook | MISSING | Rehearse actual client selection, cache/manifest correction, pinned assets, channel fixes, profile and workload safety | #5423 and release design retain GitHub prerelease wording; current client uses CDN | D05, D08, D10 | Preview 5 rehearsal; RC1 executable | P0 |
| Accepted parity/migration/v4 support position | MISSING | Publish language/tooling matrix, supported alternatives, configuration differences and support posture | #5355/#5356/#5358 exist; accepted closure was not established by reconciliation | D06, D11, D12 | Draft previews; ship-ready RC1 | P0 |
| Security/privacy/signing acceptance record | UNKNOWN | Confirm existing review/attestation locations and close findings before claiming acceptance | Trackers and signing infrastructure exist; artifact-wide notarization/privacy/security acceptance is not certified | D03, D09, D10 | Before affected enabled behavior; RC1 | P0 |

## Historical Planning And Update Discipline

The earlier December horizon is historical planning provenance; it is not the
controlling schedule. The inspected original GA-plan commit contains no dated
December calendar, so this package does not invent one or attribute a new date
to it. Preserve useful scope, architecture, and dependency information while
the team agrees the pre-Ignite program.

After decisions, update active guidance with the agreed source/contract and a
clear status. Retain old design rationale with an explicit successor reference
where useful. Do not treat an issue's age, an operational file's timestamp, or
an unmerged proposal as approval. Keep current documentation and executable
tests aligned; do not gate releases on incidental prose or generated byte identity.
