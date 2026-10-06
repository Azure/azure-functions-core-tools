# Team Decisions And Proposed Cuts

**Status:** PROPOSAL - FOR TEAM REVIEW.  
**Objective:** GA before Ignite 2026; exact dates and recommendations are not commitments.

[Executive review](README.md) | [Roadmap](roadmap.md) | [Work items](work-items.md)

Each decision is independently reviewable. All eleven entries below are **OPEN**.
Recommendations are proposals, not recorded team approvals. Record the actual
approver, rationale, retained scope, affected work-item classifications, and date
when closing an entry; do not infer authority from a reviewer's historical work.

Scope-cut approval is part of D01; the former D07 duplicated that choice and is
consolidated here. This is review organization, not approval of any feature cut.

Existing release-design decisions remain inputs: CDN-first; installer/CDN,
Homebrew, winget, and both npm names as GA-blocking; APT post-GA; roll-forward-first;
immutable shipped versions and workload unlisting rather than deletion. These
inputs do not resolve MSI, candidate acceptance, or the feature cutline.

## D01

**GA Feature Cutline**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Preview 5 must finish a selected tranche; RC1 cannot reserve major feature work for later. Feature breadth cannot consume required safety capacity. |
| Options | Retain the full intended program; approve selected [post-GA cuts](#proposed-cuts) individually; select a smaller supported program with explicit alternatives and adoption consequences. |
| Current evidence | [Original inventory](../v5-ga-plan.md) is broader than a minimum program. [Readiness guide](../v5-ga-readiness.md) has no approved cutline. Current migration, updater, quality, and channel outcomes still need delivery evidence. |
| RECOMMENDED | Agree a bounded tranche and approve/reject each proposed cut. Treat full in-place update and enabled telemetry export as targets until explicitly retained; safe update/recovery paths, diagnostics and privacy stay mandatory. Reject any cut of a retained dependency or supported workflow without accepted migration guidance. No feasibility claim without capacity and dates. |
| If deferred | Teams optimize different scopes; dependency work and release claims can diverge. |
| Work unblocked | Retained feature commitments, WI-47 stack support and WI-33 through WI-42/WI-45/WI-46 cuts. Current-path regression, intake and evidence collection do not wait for the cutline. |
| Owner / input | Francisco, Fabio, Sarah review input; accountable product approval and delivery owners must be confirmed. |

## D02

**Templating Migration Versus Supported Fallback**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Preview 4 is reliability work. Preview 5/RC1 need a coherent, fully validated scaffolding path, not half of a catalog migration. |
| Options | Retain complete user-facing migration; intentionally support the current provider path at GA; retain a bounded command migration only with an explicit complete list/select/invoke contract and working current paths for other commands. |
| Current evidence | Production new uses the legacy provider path; the func-owned catalog is not a production command consumer. [#5655](https://github.com/Azure/azure-functions-core-tools/issues/5655) and [#5670](https://github.com/Azure/azure-functions-core-tools/pull/5670) do not make internal eligibility equivalent to command migration. |
| RECOMMENDED | Keep migration out of Preview 4. If retained, finish the minimum coherent user experience in Preview 5 and qualify it before RC1, including required acquisition/adapters, command context, eligibility, selection, invocation and failures. Do not defer its prerequisites as a whole ecosystem. T1 alone is not user-visible completion. |
| Fallback experience | Current project/function scaffolding remains on its existing providers, with tested workload/template inputs and supported-stack guidance. It does not promise the new template-package lifecycle, template-first init, catalog search or new eligibility UX from the unconsumed catalog. Existing behavior still requires regression and migration evidence. |
| If deferred | Baseline tests, template contracts, and command work can target incompatible assumptions. |
| Work unblocked | WI-14 migration commitment and the optional portions of WI-34. WI-03 and current-path WI-13 validation can begin independently; fallback acceptance remains a GA scope decision. |
| Owner / input | Template implementation and product/parity input required; delivery owner and approver not assigned by this page. |

## D03

**Minimum GA Workload Compatibility Contract**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Preview 5/RC1 must install and load supported workload/profile combinations safely despite independent versions and RID packages. |
| Options | Select a minimum explicit install/load enforcement contract; retain broader metadata/evaluator and release coordination from the draft design; retain current behavior only after agreeing tested supported combinations and accepting documented gaps. |
| Current evidence | [#5673](https://github.com/Azure/azure-functions-core-tools/pull/5673) is a draft coordination design. RID pointer/implementation behavior exists. Template constraint eligibility is a distinct, narrower contract. |
| RECOMMENDED | Select tested advertised combinations and minimum known-incompatibility rejection/diagnostics. Retain additional metadata/evaluator mechanisms only if required by that contract. Do not promise rejection of every unknown future combination or approve all draft enforcement by calling the design complete. |
| If deferred | Acquisition, profile publication, loader validation, and publisher guidance lack a shared acceptance boundary. |
| Work unblocked | WI-12, WI-15, WI-16, WI-27, and their compatibility scenarios. |
| Owner / input | Workload-contract, publisher, security, and release input required; accountable owner confirm. |

## D04

**Quality Tracking And #5656 Boundary**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Establish comparisons in Preview 4 and automation by Preview 5 without appropriating existing scaffolding work. |
| Options | Add a bounded differential child under [#5353](https://github.com/Azure/azure-functions-core-tools/issues/5353); create a separate linked tracker; explicitly extend an existing tracker only with its owner's agreement and preserved acceptance scope. |
| Current evidence | [#5656](https://github.com/Azure/azure-functions-core-tools/issues/5656), assigned to satvu, covers result-based init/new/quickstart regressions. Its inspected scope and the E2E umbrella do not specify a complete pinned cross-release comparator. Real-process tests already exist. |
| RECOMMENDED | Preserve #5656. Assign explicit differential/result tracking and ownership without changing Sarah's acceptance scope by implication. Define scenarios, pinned inputs and delta classification now; the tracker-location choice is coordination, not a technical prerequisite to evidence preparation. Share existing subprocess/fixture patterns rather than require a generalized framework. |
| If deferred | Manual results remain difficult to compare; ownership and expected-delta classification remain ambiguous. |
| Work unblocked | WI-04 delivery ownership and integration of results. WI-01/02/03/05 bounded preparation is already possible; comparisons wait for selected inputs, not for a green final record. |
| Owner / input | Sarah's input for the existing #5656 boundary; Francisco/Fabio/Sarah review input for broader quality ownership, which is not assigned here. |

## D05

**winget/MSI Requirement And Acceptance Path**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Decide before Windows channel implementation; winget must be operational by RC1. |
| Options | Select an accepted portable/archive path, MSI where required, or another documented accepted packaging path after checking channel/enterprise requirements. |
| Current evidence | [Release design](../cli-release-story.md) and [#5419](https://github.com/Azure/azure-functions-core-tools/issues/5419) leave the MSI requirement open. MSI is not a separately committed channel. |
| RECOMMENDED | First obtain the actual channel/enterprise acceptance evidence; whether MSI is required is not settled by a scope vote. Then select and qualify an eligible delivery path. Do not drop winget, add an independent MSI promise, or infer acceptance from the detector. |
| If deferred | Packaging, updater detection, submission, and validation can miss a required predecessor. |
| Work unblocked | WI-07, WI-09, WI-20, Windows release/rollback procedures. |
| Owner / input | Unassigned winget tracker; release/Windows packaging and channel acceptance input required. |

## D06

**Durable/Parity, Migration, And v4 Support Position**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Selected GA scope and RC1 need a defensible per-language/tooling position, including supported alternatives for absent CLI capabilities. |
| Options | Retain required parity implementation; defer named workflows with supported v4/other-tool guidance; deliberately change/drop workflows with explicit rationale and partner agreement. |
| Current evidence | [#5355](https://github.com/Azure/azure-functions-core-tools/issues/5355), [#5356](https://github.com/Azure/azure-functions-core-tools/issues/5356), and [#5358](https://github.com/Azure/azure-functions-core-tools/issues/5358) track parity/migration/schema review. Durable workload and pack redesign are inventory items, not validated parity evidence. |
| Separate answers | Record workflow parity choices, tested migration alternatives, and v4 EOL/security-support details separately. The [release design](../cli-release-story.md) already establishes independent cadence and v4 maintenance at v5 GA; do not reopen that policy by treating all support posture as unknown. Exact EOL and security-release scope remain inputs to close. |
| RECOMMENDED | Audit affected workflows before a cut; document at parity, changed, or dropped for each. Any Durable deferral needs explicit support/migration guidance. Do not claim all Durable applications cannot run because CLI parity is incomplete. |
| If deferred | A feature cut can silently exclude adopters or overpromise v4/v5 compatibility and support. |
| Work unblocked | WI-13, WI-25, WI-26, WI-33, WI-41, WI-47, partner guidance. |
| Owner / input | Product, Durable/language/tooling, migration, and support-policy input required; owner/approver confirm. |

## D08

**Ownership, Capacity, And Committed Delivery Dates**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Pre-Ignite delivery depends on accountable outcomes and elapsed-time work, not issue counts. Assign the early channel/security/partner/migration work before Preview 4 exits, not after feature completion. |
| Options | Staff parallel required outcomes with separate review/validation capacity; narrow features to fit available capacity; acknowledge inability to meet the target if protected gates cannot be staffed/delivered. |
| Current evidence | satvu's inspected tracking overlaps update/profiles/telemetry/#5656; updater reviews have serial predecessors. castrodd is assigned Homebrew, npm, partners, and manifest work. Several required trackers are unassigned; available capacity and committed dates are not established. |
| RECOMMENDED | Obtain explicit owner acceptance, review capacity and external contacts; use estimates/dates to plan, not to certify releases. Start long-lead coordination alongside features. Decision-record gates are human work, not independently executable engineering tasks. Do not infer workload or authority from assignment. |
| If deferred | A nominally feasible feature plan can leave distribution/productization gates unresolved. |
| Work unblocked | WI-02, WI-07, WI-16 through WI-32 and scope-dependent delivery allocation. |
| Owner / input | Francisco, Fabio, Sarah review input and actual delivery/cross-team capacity information; no hierarchy inferred. |

## D09

**Telemetry Privacy, Diagnosability, And Acceptance**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Enabled export needs an accepted content/opt-out policy before shipping; RC1 also needs working diagnosability and bounded latency. |
| Options | Ship reviewed minimal telemetry with explicit scrubbing and lifecycle acceptance; retain broader taxonomy only after policy/audit closure; limit optional export while preserving supported diagnostics and revisiting the proposed MUST scope explicitly. |
| Current evidence | [#5349](https://github.com/Azure/azure-functions-core-tools/issues/5349) already tracks privacy/scrubbing and a follow-up audit. [#5677](https://github.com/Azure/azure-functions-core-tools/pull/5677) changes lifecycle/ownership; [#5522](https://github.com/Azure/azure-functions-core-tools/issues/5522) is a separate logging gap. |
| RECOMMENDED | Separate the optional export feature, exact-head technical review, privacy acceptance and minimum diagnostic observables. If export is retained, test lifecycle, content, opt-out and enabled/offline latency before inclusion. If disabled, verify no-export behavior. Removing optional export does not waive privacy/security or supported diagnostics. |
| If deferred | Lifecycle repair can be mistaken for content-policy approval, or diagnostics remain unusable. |
| Work unblocked | WI-08, WI-10, WI-11, WI-27, WI-28. |
| Owner / input | satvu on #5349; privacy/security and CLI logging input required; acceptance approver confirm. |

## D10

**Supported Matrix And Candidate Acceptance Evidence**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | Preview 4 needs declared candidate coverage; RC1 needs complete supported platform/channel/signing and latency acceptance. |
| Options | Qualify the original platform inventory with explicit supported platform/stack/channel cells; amend advertised coverage only through product/partner agreement; stage preview coverage without treating untested advertised targets as green. |
| Current evidence | Original inventory names six RID targets. [#5421](https://github.com/Azure/azure-functions-core-tools/issues/5421) does not specify the full distribution/RID matrix. Signing infrastructure exists, but all-artifact/notarization evidence is not certified. [#5440](https://github.com/Azure/azure-functions-core-tools/issues/5440) only describes archive-before-manifest publication. |
| RECOMMENDED | Define supported cells and artifact/payload acceptance early. Open the candidate input record before testing and close it with results before promotion. Select minimum latency acceptance with quality/performance input; measurements need not wait for that choice. Requalify any final version, signing or packaging change; a source freeze alone does not qualify new bytes. |
| If deferred | Reviewers cannot tell which platforms or payloads a green candidate actually supports. |
| Work unblocked | Final matrix qualification and release acceptance for WI-07/08/16/17/19/21/22. Input-record drafting, measurements, prerequisite discovery and staged tests can start earlier without claiming final approval. |
| Owner / input | Release, platform, signing/security, quality, and partner input required; accountable acceptance owner confirm. |

## D11

**Localization GA Position**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | RC1 needs an explicit supported language policy, not an implied translation commitment. |
| Options | Retain localization implementation for GA; defer translation breadth with a supported policy and guidance. |
| Current evidence | [#5361](https://github.com/Azure/azure-functions-core-tools/issues/5361) and the original inventory explicitly require a ship-or-defer decision; an approved choice was not established. |
| RECOMMENDED | Decide before scope freeze; reclassify WI-46 if implementation is retained. Accessibility remains required independently. |
| If deferred | User/docs expectations and implementation scope remain ambiguous. |
| Work unblocked | WI-43, WI-46 and documentation/support language guidance. |
| Owner / input | Product/localization and support input required; owner confirm. |

## D12

**Kubernetes/KEDA GA Position**  
**Status:** OPEN

| Field | Review surface |
| --- | --- |
| Why / checkpoint | RC1 parity closure must say whether these CLI workflows are supported, changed, or excluded. |
| Options | Retain a workload implementation; defer with supported alternatives; deliberately drop with usage-informed rationale. |
| Current evidence | [#5345](https://github.com/Azure/azure-functions-core-tools/issues/5345) asks for v4 usage investigation and a ship/drop decision; implementation is conditional on that choice. |
| RECOMMENDED | Obtain usage/parity input, decide explicitly, and reclassify/staff WI-45 if retained. Do not force implementation into GA merely because the decision issue is active. |
| If deferred | Tooling parity and partner/migration guidance cannot close coherently. |
| Work unblocked | WI-44, WI-45, WI-25/WI-26 tooling guidance. |
| Owner / input | Product/tooling, telemetry/usage, and partner input required; owner confirm. |

## Proposed Cuts

All rows are **DEFER / POST-GA proposals**, not approved decisions. Each preserves
the corresponding MUST capability/contract and is invalid if a retained GA flow
depends on the deferred work. APT and removed distribution channels are existing
release-design choices, not new cuts in this table.

| Item | Reason to defer | GA impact | Dependencies retained | Risk of deferral | Migration / compatibility implication | Team decision required |
| --- | --- | --- | --- | --- | --- | --- |
| WI-33 Durable workload/CI breadth | Reserve capacity only after reviewing affected workflows | May exclude named Durable-specific CLI workflows | Parity audit, runtime behavior, tested alternatives | Material adoption impact; evidence required before cut approval | Explicit v4/other-tool guidance and support position | D06, D01 |
| WI-34 Optional template ecosystem breadth | Beyond the explicitly retained end-to-end path | Optional management/search/sample breadth only | Required acquisition/adapters, complete retained command migration, package safety | A prerequisite inside a linked issue invalidates cutting that portion | Document exact supported inputs/UX; preserve minimum resolution/invocation | D02, D01 |
| WI-35 Quickstart extraction | Architecture relocation rather than current scaffold outcome | Keep current CLI path if qualified | Manifest/content acquisition and retained quickstart tests | Distribution/design coupling must be checked | Describe current content/version contract; no silent behavior change | D01 |
| WI-36 func doctor | Additional diagnostic automation | Manual supported troubleshooting remains necessary | Working logs/errors/help and support playbooks | More support effort | Document supported diagnostics rather than promise new command | D01 |
| WI-37 Aspire integration | Additional integration surface | No new dashboard-integration promise | Existing run/start and partner requirements | Unverified partner expectations may invalidate cut | Communicate absent integration if previously promised | D01 |
| WI-38 Schema-store registration breadth | External editor-discovery convenience | Less automated editor discovery | Required schema definitions, advertised schema URLs and compatibility audit | Deferring an advertised schema contract is not allowed under this cut | Keep valid documented/public schemas; defer only store registration breadth | D01 |
| WI-39 Settings redesign | New settings surface beyond retained compatibility | No new format/workflow promise | Current settings behavior and schema audit | Existing gaps may require targeted fixes despite cut | Document supported local.settings/host configuration and differences | D01 |
| WI-40 Meta/setup refactor | Internal packaging/composition change | Current setup remains if qualified | Safe acquisition, profiles, setup/check/output tests | Refactor may be a hidden dependency | Preserve current install/update ownership and behavior | D01 |
| WI-41 pack redesign/Azure CLI integration | Broader packaging/tool integration, not removal of required workflows | Unknown acceptable alternative until parity assessment | Executable package/deploy alternative and partner acceptance | Evidence hold: do not approve a functional packaging cut without that alternative | Explicit tested v4/other-tool path and known-gap guidance | D06, D01 |
| WI-42 Workload repository migration | Move topology after repeatable delivery is established | Current publishers continue; no publisher gap permitted | Package/RID closure, release/runbooks, named responsibility | Ownership confusion or delayed onboarding | Preserve contracts and version/publication ownership | D01, D08 |
| WI-45 Kubernetes/KEDA implementation | Conditional feature breadth, pending parity choice | Named tooling workflows may be excluded | Usage-informed decision and supported alternatives | Adoption/partner impact unknown until audit | Document change/drop and v4/other-tool guidance | D12, D01 |
| WI-46 Localization implementation | Translation breadth after policy selection | Supported language policy must be explicit | Accessibility and user/support documentation | User reach and support expectations | Do not imply translations are available | D11, D01 |

## Recording Review Outcomes

For each closed decision, record the actual choice and its evidence, approver,
remaining risks, affected WI classifications/checkpoints, and follow-up tracker.
Update current authoritative docs afterward while retaining useful historical
provenance. Open decisions are the purpose of this review package, not hidden
approvals or a reason to mark implementation ready.
