# Requirements And Work-Item Matrix

**Status:** PROPOSAL - FOR TEAM REVIEW.  
**Evidence snapshot:** reconciled CLI evidence as of 2026-10-06, not a new delivery certification.

[Executive review](README.md) | [Roadmap](roadmap.md) | [Decisions](decisions.md)

## Reading The Matrix

Derive capabilities from product contracts, the [original inventory](../v5-ga-plan.md),
the [release design](../cli-release-story.md), and the [readiness model](../v5-ga-readiness.md).
Issues and PRs are mappings afterward. Tracking existence, assignment, and green
checks do not establish delivery or validation. References outside the refreshed
issue/PR subset are scope references, not claims about their current status.

Each item has a delivery row and an acceptance row, joined by stable `WI-xx` ID.
Together they capture capability, bucket, tracking, checkpoint, lane, state,
predecessor, quality gate, distribution/productization dependency, decision,
GA class, priority, owner/input, delegation readiness, confidence/gap, and notes.

- `MUST / P0`: proposed minimum GA acceptance outcome, not necessarily a new implementation project. Decision records and qualification gates must not be counted as additional feature tranches.
- `TARGET / P1`: proposed optional feature breadth. If retained, implementation must finish in Preview 5 and qualification before RC1; TARGET does not waive its safety gates.
- `DEFER / POST-GA`: proposed cut, not an approved decision or permission to weaken safety.
- `READY`: the named bounded starting output in the delegation view has sufficient scope and inputs to begin without inventing a product decision. It does not certify the whole outcome, assign capacity, or authorize execution.
- `READY AFTER DECISION` and `READY AFTER PREDECESSOR`: name the blocking decision or delivery outcome.
- `NOT READY` and `UNKNOWN`: insufficiently defined scope or evidence; do not infer readiness.
- Confidence describes the evidence: `HIGH` for direct source/ref or inspected tracker facts, `MEDIUM` for design/tracking scope, `UNKNOWN` for unverified delivery. Each row names its remaining gap.

Checkpoints are proposed completion gates, not calendar commitments. Decision
input is not an invented delivery-owner assignment. Refresh mutable facts at
candidate approval, not simply because this document is newer.

Staging, qualification, and promotion are different steps. Open the candidate
record with selected source, artifacts, and inputs before testing; append results
and close it afterward. Tests use staged, pinned payloads, not a release already
advertised as green. Final publication consumes qualification evidence; it is
not a predecessor of producing that evidence. Cross-links below identify shared
acceptance, not a requirement to complete both sides before either can start.

## Quality

### Delivery

| ID | Required capability / bucket | Tracking / current state | Checkpoint / lane | GA / priority | Owner / input | Delegation | Confidence / gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WI-01 | Reproducible Preview 3 baseline / baseline | No dedicated outcome tracker established; shipped tag/artifact/checksum metadata observed | Preview 3 input; Preview 4 onward / quality | MUST / P0 | Quality and release owner required; unassigned here | READY | HIGH metadata; baseline executions and full workload closure not certified |
| WI-02 | Candidate selection record, then qualified readiness record / candidate cut | [#5440][i5440], [#5421][i5421] partial; TRACKING DECISION REQUIRED for combined source/artifact/workload/profile/gate evidence | Open before Preview 4 tests; close before each promotion / quality | MUST / P0 | Release owner required; partial tracker assignments do not assign this combined outcome | READY | MEDIUM tracking; record structure can be drafted, candidate and final acceptance remain unqualified |
| WI-03 | Current scaffolding regression coverage / scaffolding | [#5656][i5656] open, assigned; implementation completion not established | Before migration; Preview 5 / quality | MUST / P0 | satvu on #5656; coordinate without expanding that assignment | READY | HIGH tracker scope; results and implementation need verification |
| WI-04 | Pinned baseline-to-candidate semantic differential / cross-release quality | UNTRACKED REQUIRED WORK in inspected set; [#5353][i5353] is a possible parent, not an adequate explicit contract | Define inputs now; first comparisons Preview 4; automated Preview 5 / quality | MUST / P0 | Dedicated outcome owner required; not automatically satvu | READY AFTER PREDECESSOR | MEDIUM requirement; executions need pinned inputs, not prior completion of the candidate result record |
| WI-05 | Fast real-CLI smoke scenarios / command contracts | [#5353][i5353] umbrella; real-process pattern already exists in SetupProgramAdvisoryTests | Preview 4 onward / quality | MUST / P0 | Quality owner required; bounded scenario/fixture definition can start | READY | HIGH execution-pattern evidence; no installed-release comparator certification |
| WI-06 | Behavioral and local end-to-end journeys for advertised workflows / runtime | [#5353][i5353] open; broad install/init/start/publish scope, delivery unverified | Minimum retained journeys Preview 5; supported-matrix qualification RC1 / quality | MUST / P0 | Scenario and runtime input required; owner not established | READY AFTER PREDECESSOR | MEDIUM scope; pinned inputs and retained workflow contract needed; cloud breadth is not automatically a fast-suite gate |
| WI-07 | Release/channel validation matrix / installer coverage | [#5421][i5421] open; concrete distribution/RID acceptance matrix underspecified | Preview coverage Preview 4; complete RC1 / quality | MUST / P0 | Unassigned inspected tracker; platform and release input required | READY AFTER DECISION | HIGH tracker; support matrix and results are EVIDENCE GAP |
| WI-08 | Measured startup/common-path regression acceptance / performance | [#5354][i5354] open; targets, harness, and CI budget not established | Collect evidence in previews; accepted limits before RC1 qualification / quality | MUST / P0 | Performance/quality owner and budget input required | READY AFTER DECISION | HIGH tracker scope; no numerical limits approved; a generalized benchmark framework is not the minimum gate |

### Acceptance

| ID | Dependency / predecessor | Quality gate | Distribution / productization dependency | Decision required | Notes |
| --- | --- | --- | --- | --- | --- |
| WI-01 | Pinned artifact identity and workload/feed inputs | Verify archive integrity before use; record semantic outcomes and baseline limitations | Artifact preservation and reproducible acquisition | [D04](decisions.md#d04), [D10](decisions.md#d10) | Metadata is not execution evidence; never retroactively add features to Preview 3. |
| WI-02 | Selected source and staged artifact/input identities to open; WI-04/07/16/21 results to close | Record exact final version/source, hashes/signing, workload/RID/profile inputs and results; any rebuild or changed payload requires requalification | Closing evidence gates WI-17 promotion; notes and channel wrappers must match qualified payloads | [D04](decisions.md#d04), [D10](decisions.md#d10) | A result record is not required to be green before its tests run; archive-only promotion tracking is insufficient. |
| WI-03 | Existing template/scaffolding paths; migration changes only after baseline | #5656 result-based init/new/quickstart cases, pinned manifest, CI; do not lock in known defects | Template owner validates payload content; CLI owns orchestration contracts | [D02](decisions.md#d02), [D04](decisions.md#d04) for relationship, not a prerequisite to Sarah's existing scope | Keep six-stack/current-template coverage distinct from cross-release comparisons. |
| WI-04 | WI-01 baseline and WI-02 selected candidate inputs; agreed scenario contract | Pin the same fixtures/workloads/configuration unless a delta is explicitly under test; capture raw outcomes and classify semantic differences, new capabilities, limitations and inconclusive runs | Staged candidate payloads; append results to WI-02 before promotion | [D04](decisions.md#d04) for tracking and ownership, not permission to define the protocol | Do not appropriate #5656. New capability means candidate acceptance passed, not an invented baseline pass. |
| WI-05 | Existing subprocess isolation; bounded fixtures | Exit/status, physical structured records, stderr policy, state preservation, time-bounded owned-process cleanup | Isolated FUNC_CLI_HOME; pin local fixtures/caches for network-free cases | [D04](decisions.md#d04) for broader allocation | Immediate scoping is possible; implementation ownership still needs agreement. |
| WI-06 | Pinned candidate/fixture/workload inputs, WI-05 smoke contract and retained workflow scope; WI-04 consumes results | Init/new/run-start/invocation, errors, cancellation and cleanup for advertised combinations; qualify advertised cloud workflows separately where needed | Staged compatible payloads and managed emulator; feed results into WI-16 rather than wait for its final green status | [D01](decisions.md#d01), [D04](decisions.md#d04), [D10](decisions.md#d10) | Tests produce qualification; they do not depend on already qualified releases. |
| WI-07 | Defined matrix; WI-17 through WI-21 delivery | Install, upgrade, explicit version, integrity, package-manager ownership, negative/rollback cases per supported target | All required channels and artifact qualification | [D05](decisions.md#d05), [D10](decisions.md#d10) | #5421 is tracked, not untracked; clarify its acceptance criteria. |
| WI-08 | Repeatable pinned inputs/platforms; reviewed limits before acceptance | Measure representative startup/common commands and shutdown, including enabled/offline telemetry if shipped; investigate material regressions | Exact staged artifacts; no unapproved threshold or zero-cost promise | [D10](decisions.md#d10) for supported matrix; quality/performance input for limits | Measurement can start before a budget decision; full CI benchmark automation is a separate scope choice. |

## Features

### Delivery

| ID | Required capability / bucket | Tracking / current state | Checkpoint / lane | GA / priority | Owner / input | Delegation | Confidence / gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WI-09 | Complete in-place func update / updater feature | [#5333][i5333]; [#5609][p5609] merged; [#5610][p5610] open non-draft; [#5611][p5611], [#5612][p5612] draft | If retained: implementation Preview 5, qualification RC1 / feature | TARGET / P1 | satvu on #5333; independent review capacity required | READY AFTER PREDECESSOR | HIGH reconciled PR state; no approved minimum cutline proves in-place updating mandatory over a tested installer/manager path |
| WI-10 | Enabled telemetry export feature / telemetry | [#5677][p5677] open at 1d015bc; prior review at 3eeb7d7; [#5349][i5349] policy separate | Exact-head review now; if retained, finish Preview 5 and qualify before inclusion / feature | TARGET / P1 | satvu on #5349; exact-head reviewer/validator required | READY | HIGH head delta; optional export is distinct from mandatory privacy and supported diagnostics |
| WI-11 | Supported error/runtime diagnosability / logging | [#5522][i5522] open; empty-builder logging-provider gap identified, fix not verified | Retained implementation Preview 5; qualification RC1 / feature | MUST / P0 | Unassigned inspected tracker; logging/CLI UX input required | UNKNOWN | HIGH identified gap; minimum support observables are required, not an unselected provider or taxonomy |
| WI-12 | Reproducible compatible profiles and safe release/correction / profiles | [#5329][i5329], [#5608][p5608], [#5459][i5459]; design/publication closure not established | Retained mechanics Preview 5; qualification RC1 / feature | MUST / P0 | satvu on #5329; publication/operator ownership confirm | READY AFTER PREDECESSOR | MEDIUM design; minimum reproducibility/correction protected, automation breadth not yet approved |
| WI-13 | Working current scaffolding and accepted GA path / init-new | [#5656][i5656] and [#5655][i5655]; production new still uses legacy catalog/provider path | Current path Preview 4; chosen path complete Preview 5, qualified RC1 / feature | MUST / P0 | Scaffolding and parity input required; #5656 assignment remains scoped | READY | HIGH current-path evidence; baseline/hardening can start without deciding future migration |
| WI-14 | Func-owned templating migration / templating | [#5655][i5655], [#5670][p5670]; internal catalog exists without production command consumer | Preview 5 only if retained; complete before RC1 / feature | TARGET / P1 | Migration owner/reviewer required; not assigned by this proposal | READY AFTER DECISION | HIGH current path; resolution/invocation and migration acceptance not complete |
| WI-15 | Supported workload compatibility and actionable rejection / contracts | [#5673][p5673] draft; enforcement scope remains a decision | Choose before dependent work; retained implementation Preview 5, qualification RC1 / architecture/decision | MUST / P0 | Contract and release input required; owner not established | READY AFTER DECISION | MEDIUM design; tested support and known-incompatibility handling required, not all new draft metadata/evaluator scope |
| WI-16 | Installable workload/RID/profile closure / acquisition | [#5624][i5624], [#5440][i5440] partial; UNTRACKED REQUIRED WORK / TRACKING DECISION REQUIRED for explicit closure acceptance | Preview 4 candidate path; GA path by RC1 / distribution | MUST / P0 | Release/payload publishers required; ownership of this combined outcome confirm | READY AFTER DECISION | HIGH prerelease version metadata; payload/default-feed install and all-RID closure unverified |
| WI-47 | Retained language worker/stack/template coverage / supported stacks | [#5319][i5319]-[#5323][i5323], [#5327][i5327] original inventory; per-stack delivery is not certified by this package | Existing support Preview 4; selected implementation complete Preview 5, qualified RC1 / feature | MUST / P0 | Language/payload publisher and parity input required; owners not assigned here | READY AFTER DECISION | MEDIUM inventory; advertised platform/stack combinations must be explicit, not an implied Cartesian product |

### Acceptance

| ID | Dependency / predecessor | Quality gate | Distribution / productization dependency | Decision required | Notes |
| --- | --- | --- | --- | --- | --- |
| WI-09 | 5609 -> 5610 -> 5611 -> 5612 review/delivery if retained; recheck bases at implementation | Shipped in-place updating must meet integrity, serialization, probe/rollback, cancellation and manager-ownership gates | WI-17/WI-22 must provide a tested update/recovery path even if this feature is cut; address [#5623][i5623] for advertised preview use | [D01](decisions.md#d01), [D05](decisions.md#d05) | Proposed TARGET, not approved deferral; an advertised updater cannot ship unsafe or as an unexplained dead end. |
| WI-10 | New head review; D09 acceptance before enabled inclusion | If enabled: exact-head lifecycle, opt-out, content and shutdown/offline latency tests; if disabled: verify the no-export path | WI-28 privacy acceptance remains MUST; build-injected CLI sink, not customer connection-string precedence | [D01](decisions.md#d01), [D09](decisions.md#d09) | Earlier approval/tests do not certify 1d015bc. Export feature breadth is not the minimum privacy/diagnostics gate. |
| WI-11 | Confirm the retained support/output contract | Actionable failures, usable runtime/verbose diagnostics, no structured-output corruption or secret leakage | Support guidance and WI-28; choose provider only if needed to meet these observables | [D09](decisions.md#d09) | Do not require every ILogger call to become visible merely because an absent provider was reported. |
| WI-12 | Selected compatible profile inputs and agreed correction process | Pin/reproduce supported versions; qualify staged profiles with payloads; test correction without mutating shipped inputs | WI-16 jointly validates profiles/payloads; WI-22 rehearses correction; named publisher/operator | [D01](decisions.md#d01), [D03](decisions.md#d03) | Staged profiles precede joint tests; full registry/automatic-update pipeline is not silently mandatory. |
| WI-13 | WI-03 current-path regression evidence now; D02 before changing/committing the GA path | Existing init/new/quickstart work; preserve adoption/user source; any retained migration gets complete list/select/invoke and failure coverage | Compatible templates and accepted migration guidance | [D02](decisions.md#d02) for migration/fallback commitment, [D06](decisions.md#d06) for parity | D02 must not block testing or hardening the already shipped path. |
| WI-14 | D02; baseline WI-03; internal eligibility foundation then command migration | Same-session context/list/resolve/invoke, strict parameters, diagnostics, fail-closed constraints, non-bypassable force semantics | Template package inputs and declared behavior differences | [D02](decisions.md#d02) | T1 internal eligibility alone is not user-visible completion; keep migration out of Preview 4. |
| WI-15 | D03 selected support/rejection contract and combinations | Test advertised combinations and reject the known incompatibilities the selected contract detects with actionable diagnostics | WI-16 qualified release combinations and publisher obligations | [D03](decisions.md#d03) | Do not promise rejection of every unknown future combination or require all #5673 scope; template eligibility is separate. |
| WI-16 | Selected staged payload/profile versions, feeds, WI-15 contract and matrix; WI-06 supplies journey results | Acquire exact-version pointer implementations from intended sources and qualify each advertised platform/stack combination | Qualification feeds WI-02 and gates WI-17 advertisement; no dependency on prior public promotion | [D03](decisions.md#d03), [D10](decisions.md#d10) | Inventory can start now; tests use staged inputs. Metadata/search is not payload correctness. |
| WI-47 | D01 retained stacks, D06 parity position, WI-15/WI-16 package qualification | Scaffold, start, and invoke each advertised stack with its retained worker/template combinations; validate configuration and error paths | Platform-compatible host/worker/template payloads and accurate support/migration guidance | [D01](decisions.md#d01), [D03](decisions.md#d03), [D06](decisions.md#d06) | Do not infer Java, PowerShell, Go, or any other stack's completeness from an old milestone or template-only test. |

## Distribution

### Delivery

| ID | Required capability / bucket | Tracking / current state | Checkpoint / lane | GA / priority | Owner / input | Delegation | Confidence / gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WI-17 | CDN installers and manifest promotion / delivery | [#5439][i5439] closed CDN migration; [#5440][i5440] open; public manifest still advertises Preview 3 in snapshot | Preview 4 path; RC1 qualification; GA publication / distribution | MUST / P0 | castrodd on #5440; release acceptance owner confirm | READY AFTER PREDECESSOR | HIGH source/metadata; complete promotion acceptance not established |
| WI-18 | Both npm package names / npm | [#5415][i5415] open assigned; [#5669][p5669] reconciled implementation addresses core-tools path, not evidence for both publications | Exercise Preview 5; operational RC1 / distribution | MUST / P0 | castrodd on #5415 | READY AFTER PREDECESSOR | HIGH tracker; current publication and dual-name installation unverified |
| WI-19 | Homebrew distribution / macOS channel | [#5414][i5414] open assigned; completed delivery not established | Start acceptance/delivery planning Preview 4; exercise Preview 5; operational RC1 / distribution | MUST / P0 | castrodd on #5414; external acceptance input required | READY AFTER PREDECESSOR | HIGH tracker; scoping can start, install/upgrade qualification needs actual macOS artifacts |
| WI-20 | winget distribution and accepted packaging / Windows channel | [#5419][i5419] open unassigned; MSI requirement unresolved | Decide before channel delivery; operational RC1 / distribution | MUST / P0 | Delivery owner and winget acceptance input required | READY AFTER DECISION | HIGH tracker/design; artifact format and acceptance path open |
| WI-21 | Signing, notarization, user verification / artifact trust | [#5416][i5416] open unassigned; signing infrastructure exists, artifact qualification not certified | Supported candidate checks; complete RC1 / distribution | MUST / P0 | Signing/notarization and release owner required | READY AFTER DECISION | HIGH infrastructure; every-artifact signature/notary evidence missing |
| WI-22 | Rehearsed per-channel release/rollback / release safety | [#5423][i5423] open unassigned; policy exists, CDN-aware operational procedure underspecified | Draft now; rehearse staged paths Preview 5; executable RC1 / productization | MUST / P0 | Release operator and channel-owner input required | READY AFTER PREDECESSOR | HIGH policy; drafting is not a completed executable/rehearsed procedure |

### Acceptance

| ID | Dependency / predecessor | Quality gate | Distribution / productization dependency | Decision required | Notes |
| --- | --- | --- | --- | --- | --- |
| WI-17 | Staged versioned artifacts for validation; closed WI-02/WI-16/WI-21 evidence before promotion | Correct pin/latest/prerelease behavior and integrity; a tested manager or installer update/recovery path; requalify final versioned/signed payloads after any rebuild | CDN cache behavior, qualified release notes, WI-22 staged rehearsal | [D01](decisions.md#d01) if in-place update is cut, [D10](decisions.md#d10) for acceptance | Staging does not require public manifest promotion; correct archives are func-<rid>.zip or .tar.gz. |
| WI-18 | Qualified CLI artifacts, publisher path, both package definitions | Install/launch/update for both azure-functions-cli and azure-functions-core-tools; manager ownership respected | Trusted publication, release notes, npm correction procedure | [D01](decisions.md#d01), [D05](decisions.md#d05) where detector behavior overlaps | Do not infer publication success from #5669 or unavailable metadata. |
| WI-19 | Formula/tap delivery and qualified macOS archives | Fresh install, upgrade, native architecture, version pin/correction, PATH behavior | External channel acceptance and notarized payloads | [D10](decisions.md#d10) | Assigned does not mean built, submitted, accepted, or validated. |
| WI-20 | D05; qualified Windows artifacts; channel submission | Accepted package shape plus install/upgrade/detection and correction cases | External winget review/acceptance and possible MSI work | [D05](decisions.md#d05) | MSI is unresolved, not an independently added GA channel. |
| WI-21 | D10 supported matrix and signing acceptance; staged payloads | Inspect signatures/integrity on every downloadable target; verify macOS notarization and user instructions | Signing service/certification input; WI-27 security review | [D10](decisions.md#d10) | Do not convert sign-app pipeline presence into published-artifact certification. |
| WI-22 | Current client contracts and staged channel/profile/workload inputs; actual delivery paths to finish | Rehearse correction with pinned previous versions, manifest/cache behavior, tested update or reinstall guidance, and workload unlisting | Completed rehearsal gates WI-17 final promotion; roll-forward-first, immutable versions, never delete resolved workloads | [D05](decisions.md#d05), [D10](decisions.md#d10) | Draft/rehearse before public promotion; GitHub prerelease flags alone do not correct CDN selection. |

## Productization

### Delivery

| ID | Required capability / bucket | Tracking / current state | Checkpoint / lane | GA / priority | Owner / input | Delegation | Confidence / gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WI-23 | Partner feedback and blocking-concern closure / embedding tools | [#5344][i5344] open assigned; completion/feedback closure not established | Start Preview 4; close RC1 / productization | MUST / P0 | castrodd on #5344; partner responses required | READY | HIGH tracker; outreach and acceptance evidence missing |
| WI-24 | Required ADO task/GitHub Action adoption paths / partner integration | [#5424][i5424] open unassigned | Agree supported paths early; implementation Preview 5, qualify RC1 / productization | MUST / P0 | Integration maintainers and release input required | READY AFTER DECISION | HIGH tracker scope; required installation/CI paths protected, not a blanket requirement for every partner to release new tooling |
| WI-25 | Language/tooling parity and Durable position / parity | [#5355][i5355] open; [#5340][i5340], [#5341][i5341] scope references; accepted parity position absent | Decide before dependent scope; close RC1 / productization | MUST / P0 | Product and language/tooling input required; owner unassigned | READY AFTER DECISION | HIGH required position; validated parity matrix and accepted gaps missing |
| WI-26 | Migration, schema compatibility, v4 support / transition | [#5356][i5356], [#5358][i5358] open; [#5357][i5357], [#5359][i5359] scope references | Draft in previews; ship-ready RC1 / productization | MUST / P0 | Migration/support and stack-owner input required; not assigned here | READY AFTER DECISION | HIGH inspected tracker scope; guidance/schema audits and support decision missing |
| WI-27 | Security and supply-chain review / safety | [#5360][i5360], [#5430][i5430] open unassigned | Start Preview 4; accepted RC1 / productization | MUST / P0 | Security reviewer and payload/publisher input required | READY | HIGH tracking; audit scope, findings, and accepted mitigations need delivery |
| WI-28 | Telemetry privacy and implementation audit / privacy | [#5349][i5349] open assigned; privacy/scrubbing and follow-up audit explicitly tracked | Before shipping enabled behavior; close RC1 / productization | MUST / P0 | satvu on #5349; privacy acceptance input required | READY AFTER DECISION | HIGH tracker scope; policy and audit closure not established |
| WI-29 | Accessible, non-TTY and NO_COLOR behavior / accessibility | [#5362][i5362] inventory scope; current audit closure not verified | Validate previews; sufficient coverage RC1 / productization | MUST / P0 | Accessibility/CLI UX input and outcome owner required | READY | MEDIUM contract; audit results and remediation gaps unknown |
| WI-30 | Review relevant docs and fix release-critical current guidance / documentation | [#5350][i5350], [#5351][i5351], [#5352][i5352] scope; targeted stale guidance identified | Inventory/source-correctness work now; decision-dependent guidance and final notes before qualification / documentation | MUST / P0 | Documentation outcome owner required; relevant scope decisions precede only affected updates | READY | HIGH identified discrepancies; all-relevant-docs P0 review retained, not a mandatory rewrite of historical design records |
| WI-31 | GA cutover and support handoff / release operation | [#5363][i5363] open unassigned | Prepare before RC1; execute GA / productization | MUST / P0 | Release approver/operator and support input required | READY AFTER PREDECESSOR | HIGH tracker; cutover/go-no-go/support readiness not established |

### Acceptance

| ID | Dependency / predecessor | Quality gate | Distribution / productization dependency | Decision required | Notes |
| --- | --- | --- | --- | --- | --- |
| WI-23 | Retained scope and installation contracts | Record affected partner tests, blocking feedback, and resolution | External teams need response time independent of feature coding | [D01](decisions.md#d01), [D08](decisions.md#d08) | Do not claim partners must all release new tooling; resolve actual adoption blockers. |
| WI-24 | Agreed required partner paths, then staged WI-17/WI-18 inputs | Qualify those installation/CI paths and preserve supported v4 use; resolve actual adoption blockers | Maintainer acceptance of required paths; notification/intake remains WI-23 | [D01](decisions.md#d01), [D06](decisions.md#d06) | Begin maintainer evidence collection now; neither templating migration nor all partner releases is an automatic predecessor. |
| WI-25 | D01, D06; per-stack and tooling evidence | Living matrix: at parity, changed with guidance, or dropped with rationale | Migration and supported v4 path for affected workflows | [D06](decisions.md#d06), [D12](decisions.md#d12) | Assess Durable-specific CLI workflows; do not infer all Durable apps cannot run. |
| WI-26 | WI-25 accepted workflow choices; audit current configs and draft guidance in parallel | Command map, differences, known gaps and host.json/local.settings.json audit | Existing v4 maintenance policy, still-open EOL/security scope, external docs and partner guidance | [D06](decisions.md#d06), [D01](decisions.md#d01) | Migration evidence gathering does not wait for final parity choices; no exact v4 EOL date is approved here. |
| WI-27 | Scoped token/feed/package/signing review | Document findings and tested mitigations; verify trust before extraction as retained contract requires | WI-15, WI-21, WI-28; official and development-package policy | [D03](decisions.md#d03), [D09](decisions.md#d09), [D10](decisions.md#d10) | Audit preparation can start; architecture-dependent fixes await decisions. |
| WI-28 | D09 scope; WI-10 only if export is enabled; diagnostics path independently | For shipped behavior: verify no secret/PII leakage, opt-out and lifecycle; verify no-export behavior if disabled; taxonomy/retention only for retained collection | Privacy/security acceptance and supported diagnostics remain protected | [D09](decisions.md#d09) | Already tracked in #5349; optional export removal cannot waive privacy or turn lifecycle repair into policy approval. |
| WI-29 | Supported terminal/output modes; representative tests | NO_COLOR, redirected/non-TTY output, prompts/cancellation, assistive use and structured records | Document limitations and remediate release blockers | [D10](decisions.md#d10) for supported matrix | Audit evidence work is ready; completed usability is not assumed. |
| WI-30 | Current source for factual corrections; only relevant decisions for scope-dependent guidance | Review all relevant docs; remediate current release-critical contradictions, broken contractual guidance and final-version examples; preserve historical rationale | External docs, migration, partner guidance, release/playbooks | [D01](decisions.md#d01), [D02](decisions.md#d02), [D03](decisions.md#d03), [D05](decisions.md#d05) where the update depends on them | No need to wait for every decision to inventory/fix a known source mismatch; P0 follow-up remains protected. |
| WI-31 | Plan cutover during previews; execute only after final artifact qualification, WI-22 and WI-26 | Qualify exact final versioned/signed bytes and channel packaging; verify published payloads match; any rebuild/re-sign or wrapper change triggers relevant revalidation before advertisement | Agreed branch/tag, final notes, triage/SLA and operational ownership | [D01](decisions.md#d01), [D08](decisions.md#d08) | RC-to-GA version/tag changes cannot inherit a checksum certificate for different bytes. |

## Architecture And Decisions

### Delivery

| ID | Required capability / bucket | Tracking / current state | Checkpoint / lane | GA / priority | Owner / input | Delegation | Confidence / gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WI-32 | Accepted scope and accountable outcome record / program | TRACKING DECISION REQUIRED; this review is not execution tracking or owner assignment | Before dependent delivery; release accountability closed RC1 / architecture/decision | MUST / P0 | Francisco, Fabio, Sarah review input; human approver and accountable owners confirm | NOT READY | HIGH required decision record; capacity/dates are scheduling inputs, not another implementation feature or measured release gate |
| WI-43 | Localization ship-or-defer record / localization | [#5361][i5361] original scope; no approved GA choice established | Before scope freeze; RC1 / architecture/decision | MUST / P0 | Human product/localization input required; approver not established | NOT READY | MEDIUM inventory; evidence preparation can start, decision itself cannot be delegated as settled |
| WI-44 | Kubernetes/KEDA parity record / tooling | [#5345][i5345] original scope; ship-or-drop decision not established | Before parity closure; RC1 / architecture/decision | MUST / P0 | Human product/tooling and usage input required; approver not established | NOT READY | MEDIUM inventory; usage collection is not a ship/drop approval |

### Acceptance

| ID | Dependency / predecessor | Quality gate | Distribution / productization dependency | Decision required | Notes |
| --- | --- | --- | --- | --- | --- |
| WI-32 | Reviewable inventory, named human approver and accountable outcome owners | Record approved scope, gaps and acceptance responsibilities; retained feature implementation complete in Preview 5 | Capacity/reviewer/external dates inform scheduling without substituting for actual gates | [D01](decisions.md#d01), [D08](decisions.md#d08) | This is a decision/accountability gate, not additional engineering implementation or a promise that capacity estimates must be perfect. |
| WI-43 | Product/localization scope input | Record supported language policy and test/update obligations if retained | User documentation and support expectations | [D11](decisions.md#d11) | Decision is proposed MUST; translation implementation is separately proposed DEFER. |
| WI-44 | v4 usage/parity assessment and scope input | Record ship/drop rationale and affected workflow guidance | Migration, v4 support, partner/tooling expectations | [D12](decisions.md#d12) | Decision is proposed MUST; new workload implementation is not automatically required. |

## Proposed Deferrals

Every row is **DEFER / POST-GA proposed**, subject to the linked decision and
[risk/impact review](decisions.md#proposed-cuts). Existing supported behavior,
contracts, safety gates, and migration obligations remain in MUST items.

### Delivery

| ID | Required capability / bucket | Tracking / current state | Checkpoint / lane | GA / priority | Owner / input | Delegation | Confidence / gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| WI-33 | Durable-specific CLI workload/CI breadth / Durable | [#5340][i5340], [#5341][i5341] inventory; delivery not certified | Post-GA if approved / feature | DEFER / POST-GA | Durable/product/parity input required; owner not inferred | READY AFTER DECISION | MEDIUM scope; impact must be assessed before cut |
| WI-34 | Optional template management/discovery/sample breadth / templating | [#5629][i5629], [#5631][i5631], [#5632][i5632], [#5636][i5636], [#5651][i5651], [#5653][i5653] intersecting scope references, not a blanket cut of each issue | Post-GA only for portions outside the retained path / feature | DEFER / POST-GA | Template/product input required; owner not inferred | READY AFTER DECISION | MEDIUM designs; required acquisition, adapters, resolution and invocation must be excluded from this cut |
| WI-35 | Quickstart-as-workload extraction / quickstart | [#5324][i5324], [#5325][i5325], [#5640][i5640] scope; current path remains in CLI | Post-GA if approved / feature | DEFER / POST-GA | Quickstart/release input required; owner not inferred | READY AFTER DECISION | HIGH current path; no promise that extraction is dependency-free |
| WI-36 | func doctor / diagnostics convenience | [#5347][i5347] inventory; delivered capability not established | Post-GA if approved / feature | DEFER / POST-GA | Product/diagnostics input required; owner not inferred | READY AFTER DECISION | MEDIUM scope; support guidance must cover retained diagnostics |
| WI-37 | Aspire dashboard integration / integration convenience | [#5380][i5380] inventory; delivery not established | Post-GA if approved / feature | DEFER / POST-GA | Product/integration input required; owner not inferred | READY AFTER DECISION | MEDIUM scope; partner dependency assessment pending |
| WI-38 | Schema-store publication / editor distribution | [#5348][i5348], [#4936][i4936] inventory | Post-GA if approved / documentation | DEFER / POST-GA | Schema/editor input required; owner not inferred | READY AFTER DECISION | MEDIUM scope; required schema contracts/audits remain MUST |
| WI-39 | Settings redesign / configuration breadth | [#5343][i5343] design scope | Post-GA if approved / feature | DEFER / POST-GA | Configuration/product input required; owner not inferred | READY AFTER DECISION | MEDIUM scope; current compatibility must still be audited |
| WI-40 | Meta-package/setup refactor / internal packaging | [#5383][i5383], [#5384][i5384] design scope | Post-GA if approved / feature | DEFER / POST-GA | Setup/package input required; owner not inferred | READY AFTER DECISION | MEDIUM scope; retained setup behavior requires validation |
| WI-41 | func pack redesign/Azure CLI integration / packaging breadth | [#5342][i5342] design scope; current hidden pack stub is not full parity | Post-GA if approved / feature | DEFER / POST-GA | Packaging/Azure CLI/product input required; owner not inferred | READY AFTER DECISION | HIGH source gap; supported alternative and parity impact require decision |
| WI-42 | Workload migration to owner repositories / repository topology | [#5346][i5346] inventory | Post-GA if approved / architecture/decision | DEFER / POST-GA | Current publishers and payload owners input required; owner not inferred | READY AFTER DECISION | MEDIUM scope; release responsibility must remain explicit |
| WI-45 | New Kubernetes/KEDA implementation / tooling breadth | [#5345][i5345] decision parent; implementation tracking depends on chosen scope | Post-GA if approved / feature | DEFER / POST-GA | Tooling/product input required; delivery owner unknown | READY AFTER DECISION | UNKNOWN implementation scope until D12 |
| WI-46 | Localization implementation / translated experience | [#5361][i5361] decision parent; retained scope unknown | Post-GA if approved / feature | DEFER / POST-GA | Localization/product input required; delivery owner unknown | READY AFTER DECISION | UNKNOWN implementation scope until D11 |

### Acceptance

| ID | Dependency / predecessor | Quality gate | Distribution / productization dependency | Decision required | Notes |
| --- | --- | --- | --- | --- | --- |
| WI-33 | WI-25 accepted Durable parity position | Verify retained runtime/workflows and supported alternatives before approving the cut | v4 support and migration guidance for affected CLI workflows | [D06](decisions.md#d06), [D01](decisions.md#d01) | Evidence hold on impact/alternatives; not a generic low-risk cut or proof all Durable apps fail. |
| WI-34 | D02 dependency map identifying the minimum retained path, then scope approval | Preserve required package acquisition/adapters and complete list/select/invoke for any retained migration; defer only optional management/search/sample breadth | Retained template/publisher contracts and supported-input documentation | [D02](decisions.md#d02), [D01](decisions.md#d01) | A linked issue can contain both prerequisite and optional scope; do not defer it wholesale. |
| WI-35 | Qualified current quickstart path and D01 | Pin manifests; validate retained scaffold behavior and cache/network failures | Current content distribution and owner validation remain supported | [D01](decisions.md#d01) | Architecture cleanup moves; existing quickstart quality does not. |
| WI-36 | Explicit feature cut | Diagnose retained failures through working logs/help and documented steps | Support playbooks and future diagnostic API choice | [D01](decisions.md#d01) | Logging/privacy MUST work cannot be deferred under this label. |
| WI-37 | Partner impact check and explicit cut | Preserve supported run/start and existing integration expectations | Communicate absent integration where promised | [D01](decisions.md#d01) | Convenience is a proposal rationale, not proof of no affected users. |
| WI-38 | Required schemas and WI-26 audit retained | Validate contracts/examples and any currently advertised schema URI; do not waive compatibility or a promised public contract | External schema-store registration may wait; valid documented/public schemas cannot silently disappear | [D01](decisions.md#d01) | Narrow cut to editor-store distribution convenience, not all schema hosting/publication. |
| WI-39 | WI-26 current settings compatibility | Existing host.json/local.settings.json flows pass with declared differences | Migration guidance for absent/new settings formats | [D01](decisions.md#d01) | No new .env/configuration promise is created by this package. |
| WI-40 | WI-12/WI-16 current setup correctness | Current setup install/check/JSON/negative cases remain green; retain any refactor portion needed to fix a supported flow | Compatible workload acquisition and profiles | [D01](decisions.md#d01) | Defers internal refactoring breadth, not a required correctness fix hidden inside it. |
| WI-41 | WI-25 identifies affected workflows and validates the actual supported package/deploy alternative before cut approval | Preserve an executable supported packaging/deployment path; do not call the hidden stub parity | Explicit migration/support and partner acceptance | [D06](decisions.md#d06), [D01](decisions.md#d01) | Cut only the redesign/integration breadth; EVIDENCE GAP on acceptable alternatives prevents approving a functional packaging cut. |
| WI-42 | Named current publishers and payload ownership | Repeatable packaging/publication and compatibility qualification | WI-16/WI-22 runbooks and support handoff remain required | [D01](decisions.md#d01), [D08](decisions.md#d08) | Moving source ownership is separate from shipping valid packages. |
| WI-45 | WI-44/D12 choice and approved parity treatment | Validate supported alternative; later shipping choice needs full workload tests | Tooling migration/v4 guidance and new publisher/pipeline if retained | [D12](decisions.md#d12), [D01](decisions.md#d01) | If ship-at-GA is chosen, reclassify and staff; do not silently keep DEFER. |
| WI-46 | WI-43/D11 language-policy decision | Supported language/accessibility behavior remains acceptable | Docs/support expectations; translation process if retained | [D11](decisions.md#d11), [D01](decisions.md#d01) | If required for GA, reclassify before scope freeze. |

## Delegation View

These groups describe scoped starting outputs, not permission to execute or proof
of capacity. A READY slice does not certify its parent outcome; final acceptance
still needs its stated evidence. Preparatory slices can start even when final
delivery is decision- or predecessor-blocked.

| Group | Work / input |
| --- | --- |
| READY TO DELEGATE | WI-01: inventory pinned baseline artifacts/payload inputs and gaps; WI-02: draft the source/artifact/input/result record; WI-03: existing assigned #5656 result tests; WI-05: scoped help/version/setup subprocess smoke contract; WI-10: review the exact reconciled PR-head delta, not policy approval; WI-13: existing-path hardening/regressions; WI-23: partner notification and blocking-input intake; WI-27: current token/feed/package controls inventory and review-slot preparation; WI-29: existing non-TTY/NO_COLOR/output audit; WI-30: documentation inventory and independently verifiable source corrections. |
| READY AFTER DECISION | WI-07/08 final matrix/latency acceptance; WI-14/15/16/47 retained migration, compatibility and advertised combinations; WI-20/21 channel/signing acceptance; WI-24 required partner paths; WI-25/26/28 parity, migration and privacy closure; all proposed DEFER items after scope approval. |
| READY AFTER PREDECESSOR | WI-04/06 executions after selected pinned inputs; WI-09 updater implementation/review chain if retained; WI-12 profile correction qualification; WI-17/18/19 delivery qualification after staged artifacts; WI-22 rehearsal after staged paths; WI-31 actual cutover after closed gates. |
| HUMAN / CROSS-TEAM COORDINATION | WI-32/43/44 and the eleven OPEN decisions require named human input/approval. Channel acceptance, signing/notarization, security/privacy, partner feedback, parity/support and go/no-go are not assigned to an engineer merely by being listed here. |
| NOT READY / EVIDENCE GAP | WI-32/43/44 are not executable approval tasks for a separate engineer without human input; WI-11's desired diagnostic path is UNKNOWN. Complete candidate qualification, payload closure, dates and available capacity are not established. |

Start-now preparation outside the READY rows is deliberately narrower: WI-04's
scenario/input protocol, WI-08's measurement plan, WI-19/20/21's channel/signing
prerequisite discovery, WI-22's CDN-aware draft, WI-24's maintainer intake, and
WI-25/26's current-workflow/schema evidence can be collected without claiming
final scope or acceptance. These slices do not relabel the full blocked outcome
as READY or create new delivery-owner assignments.

The updater has serial review predecessors. Inspected tracking overlaps through
satvu for update, profiles design, telemetry design, and #5656, and through
castrodd for manifest publication, npm, Homebrew, and partners. This demonstrates
assignment concentration, not staffing estimates, utilization, or a committed date.

## Evidence Corrections

The reconciliation used product `vnext` at
[`f46197210284777cfd8b0f426c00fe2c650ea885`](https://github.com/Azure/azure-functions-core-tools/commit/f46197210284777cfd8b0f426c00fe2c650ea885).
The planning guide is a proposal, not an approved feature cutline.

- #5609 is merged; #5610 is open non-draft; #5611/#5612 remain drafts in the snapshot.
- #5677's reviewed head was `3eeb7d71573e5e805ee148e264b81b52f4111e8e`; its reconciled head is `1d015bc1b9b7cb80d3e064d544aca23c39893459`. Earlier review/tests do not certify the latter.
- [SetupProgramAdvisoryTests](../../test/Func.Tests/Commands/Setup/SetupProgramAdvisoryTests.cs) already executes a real CLI child process with isolated homes/environment, output capture, state checks, and owned-process cleanup. It is not a completed installed-release differential harness.
- `FUNC_CLI_HOME` is the home override. Workload search/quickstart acquisition can need catalogs, manifests, or pinned caches; local-first is not automatically network-free.
- Public CDN metadata advertises Preview 3 with `stable: null`; checked Preview 3 archives/checksum sidecars exist. The baseline tag is [`f8f54ba328758b2165d835930e391af23e60f090`](https://github.com/Azure/azure-functions-core-tools/commit/f8f54ba328758b2165d835930e391af23e60f090). Metadata alone is not an archive execution or full payload certificate.
- Public Preview 4 candidate availability was not established by the inspected paths; this does not prove internal candidates do not exist. Current version props/release notes still naming Preview 3 cannot identify a new candidate alone.
- Public prerelease-feed Host pointer and win-x64 implementation version metadata exists for `4.1048.200-preview.3`. All-payload/default-feed installability is not established; search absence does not rule out unlisted packages.
- Homebrew #5414, npm #5415, partners #5344, and manifest #5440 are assigned to castrodd in the snapshot. Publication, partner closure, and committed dates remain separate evidence.

[i4936]: https://github.com/Azure/azure-functions-core-tools/issues/4936
[i5319]: https://github.com/Azure/azure-functions-core-tools/issues/5319
[i5323]: https://github.com/Azure/azure-functions-core-tools/issues/5323
[i5324]: https://github.com/Azure/azure-functions-core-tools/issues/5324
[i5325]: https://github.com/Azure/azure-functions-core-tools/issues/5325
[i5327]: https://github.com/Azure/azure-functions-core-tools/issues/5327
[i5329]: https://github.com/Azure/azure-functions-core-tools/issues/5329
[i5333]: https://github.com/Azure/azure-functions-core-tools/issues/5333
[i5340]: https://github.com/Azure/azure-functions-core-tools/issues/5340
[i5341]: https://github.com/Azure/azure-functions-core-tools/issues/5341
[i5342]: https://github.com/Azure/azure-functions-core-tools/issues/5342
[i5343]: https://github.com/Azure/azure-functions-core-tools/issues/5343
[i5344]: https://github.com/Azure/azure-functions-core-tools/issues/5344
[i5345]: https://github.com/Azure/azure-functions-core-tools/issues/5345
[i5346]: https://github.com/Azure/azure-functions-core-tools/issues/5346
[i5347]: https://github.com/Azure/azure-functions-core-tools/issues/5347
[i5348]: https://github.com/Azure/azure-functions-core-tools/issues/5348
[i5349]: https://github.com/Azure/azure-functions-core-tools/issues/5349
[i5350]: https://github.com/Azure/azure-functions-core-tools/issues/5350
[i5351]: https://github.com/Azure/azure-functions-core-tools/issues/5351
[i5352]: https://github.com/Azure/azure-functions-core-tools/issues/5352
[i5353]: https://github.com/Azure/azure-functions-core-tools/issues/5353
[i5354]: https://github.com/Azure/azure-functions-core-tools/issues/5354
[i5355]: https://github.com/Azure/azure-functions-core-tools/issues/5355
[i5356]: https://github.com/Azure/azure-functions-core-tools/issues/5356
[i5357]: https://github.com/Azure/azure-functions-core-tools/issues/5357
[i5358]: https://github.com/Azure/azure-functions-core-tools/issues/5358
[i5359]: https://github.com/Azure/azure-functions-core-tools/issues/5359
[i5360]: https://github.com/Azure/azure-functions-core-tools/issues/5360
[i5361]: https://github.com/Azure/azure-functions-core-tools/issues/5361
[i5362]: https://github.com/Azure/azure-functions-core-tools/issues/5362
[i5363]: https://github.com/Azure/azure-functions-core-tools/issues/5363
[i5380]: https://github.com/Azure/azure-functions-core-tools/issues/5380
[i5383]: https://github.com/Azure/azure-functions-core-tools/issues/5383
[i5384]: https://github.com/Azure/azure-functions-core-tools/issues/5384
[i5414]: https://github.com/Azure/azure-functions-core-tools/issues/5414
[i5415]: https://github.com/Azure/azure-functions-core-tools/issues/5415
[i5416]: https://github.com/Azure/azure-functions-core-tools/issues/5416
[i5419]: https://github.com/Azure/azure-functions-core-tools/issues/5419
[i5421]: https://github.com/Azure/azure-functions-core-tools/issues/5421
[i5423]: https://github.com/Azure/azure-functions-core-tools/issues/5423
[i5424]: https://github.com/Azure/azure-functions-core-tools/issues/5424
[i5430]: https://github.com/Azure/azure-functions-core-tools/issues/5430
[i5439]: https://github.com/Azure/azure-functions-core-tools/issues/5439
[i5440]: https://github.com/Azure/azure-functions-core-tools/issues/5440
[i5459]: https://github.com/Azure/azure-functions-core-tools/issues/5459
[i5522]: https://github.com/Azure/azure-functions-core-tools/issues/5522
[i5623]: https://github.com/Azure/azure-functions-core-tools/issues/5623
[i5624]: https://github.com/Azure/azure-functions-core-tools/issues/5624
[i5629]: https://github.com/Azure/azure-functions-core-tools/issues/5629
[i5631]: https://github.com/Azure/azure-functions-core-tools/issues/5631
[i5632]: https://github.com/Azure/azure-functions-core-tools/issues/5632
[i5636]: https://github.com/Azure/azure-functions-core-tools/issues/5636
[i5640]: https://github.com/Azure/azure-functions-core-tools/issues/5640
[i5651]: https://github.com/Azure/azure-functions-core-tools/issues/5651
[i5653]: https://github.com/Azure/azure-functions-core-tools/issues/5653
[i5655]: https://github.com/Azure/azure-functions-core-tools/issues/5655
[i5656]: https://github.com/Azure/azure-functions-core-tools/issues/5656
[p5608]: https://github.com/Azure/azure-functions-core-tools/pull/5608
[p5609]: https://github.com/Azure/azure-functions-core-tools/pull/5609
[p5610]: https://github.com/Azure/azure-functions-core-tools/pull/5610
[p5611]: https://github.com/Azure/azure-functions-core-tools/pull/5611
[p5612]: https://github.com/Azure/azure-functions-core-tools/pull/5612
[p5669]: https://github.com/Azure/azure-functions-core-tools/pull/5669
[p5670]: https://github.com/Azure/azure-functions-core-tools/pull/5670
[p5673]: https://github.com/Azure/azure-functions-core-tools/pull/5673
[p5677]: https://github.com/Azure/azure-functions-core-tools/pull/5677
