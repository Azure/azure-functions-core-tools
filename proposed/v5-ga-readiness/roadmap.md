# CLI v5: Milestone Roadmap

**Status:** PROPOSAL - FOR TEAM REVIEW.
**Planning status:** Proposed milestones and priorities, subject to team review.

[Entry guide](../v5-ga-readiness.md) | [Decisions](decisions.md) | [Issues needing input](#issues-needing-input) | [Needs triage](#needs-triage) | [Tracking gaps](#release-requirements-that-need-clearer-tracking)

**Inventory status:** Requires team validation. This roadmap uses the issue and PR
evidence available on October 6, 2026. Needs Triage items have not been confirmed
as active work. Placed issues were open in that evidence; confirm current state,
scope, assignments and Project membership before scheduling. This is not a
complete live inventory. Titles are shortened descriptions; PRs provide
implementation context, not issue placement.

## Proposed Work By Release

**Placements and priorities are proposals, not existing GitHub milestones or team
commitments.** P0 identifies work currently proposed as required for release
readiness. P1 identifies work currently proposed as a release target but requiring
team agreement. Priority applies only to the release-relevant scope of an issue,
not automatically to every item in a broader epic. An input request can coexist
with a proposed destination. Each placed issue has one completion column.

| Workstream | Preview 4 | Preview 5 | RC1 | GA | Post-GA / TBD |
| --- | --- | --- | --- | --- | --- |
| Templates / quickstarts | Current-path reliability; no migration commitment | No new template issue committed; evaluate TBD leads | Qualify the agreed path and stacks | No new implementation | **Needs triage**<br>[#5655](https://github.com/Azure/azure-functions-core-tools/issues/5655) migration; [#5633](https://github.com/Azure/azure-functions-core-tools/issues/5633) strict parameters<br>[#5629](https://github.com/Azure/azure-functions-core-tools/issues/5629)/[#5631](https://github.com/Azure/azure-functions-core-tools/issues/5631)/[#5632](https://github.com/Azure/azure-functions-core-tools/issues/5632) lifecycle<br>[#5636](https://github.com/Azure/azure-functions-core-tools/issues/5636) init; [#5651](https://github.com/Azure/azure-functions-core-tools/issues/5651) samples; [#5653](https://github.com/Azure/azure-functions-core-tools/issues/5653) browse |
| Telemetry / diagnostics | No export feature committed | **P0**<br>[#5522](https://github.com/Azure/azure-functions-core-tools/issues/5522) usable diagnostics<br>[#5349](https://github.com/Azure/azure-functions-core-tools/issues/5349) policy/privacy before enabled inclusion | Verify privacy and latency | No new export behavior | Export is a **P1 target**, not approved inclusion or omission; PR #5677 below |
| Quality | Start baseline comparison and smoke coverage | **P0**<br>[#5656](https://github.com/Azure/azure-functions-core-tools/issues/5656) CLI-functional regressions<br>[#5353](https://github.com/Azure/azure-functions-core-tools/issues/5353) required journeys | **P0**<br>[#5354](https://github.com/Azure/azure-functions-core-tools/issues/5354) latency acceptance<br>[#5421](https://github.com/Azure/azure-functions-core-tools/issues/5421) install/channel matrix | Verify published artifact identity | Broader framework and exhaustive content testing are not automatically blockers |
| Updater | **P0**<br>[#5623](https://github.com/Azure/azure-functions-core-tools/issues/5623) preview dead end; test a supported recovery path | **P1**<br>[#5333](https://github.com/Azure/azure-functions-core-tools/issues/5333) complete in-place update if retained | Qualify the supported path | No new implementation | Complete updater retention needs scope agreement |
| Workloads / profiles | **P0**<br>[#5624](https://github.com/Azure/azure-functions-core-tools/issues/5624) clean-machine Host/RID acquisition | **P0**<br>[#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329) minimum profile release/correction | Qualify advertised combinations | No changed payloads without checks | **Needs triage**<br>[#5459](https://github.com/Azure/azure-functions-core-tools/issues/5459) registry/release scope; more leads below |
| Distribution | Start acceptance and staged-artifact work | Finish required implementation | **P0**<br>[#5414](https://github.com/Azure/azure-functions-core-tools/issues/5414) Homebrew<br>[#5415](https://github.com/Azure/azure-functions-core-tools/issues/5415) both npm names<br>[#5419](https://github.com/Azure/azure-functions-core-tools/issues/5419) winget | Publish qualified channels | APT after GA is established; MSI/winget requirement still open |
| Security / signing | Start review/signing prerequisites | Fix gaps before qualification | **P0**<br>[#5416](https://github.com/Azure/azure-functions-core-tools/issues/5416) signing/notarization<br>[#5360](https://github.com/Azure/azure-functions-core-tools/issues/5360) security review<br>[#5430](https://github.com/Azure/azure-functions-core-tools/issues/5430) publisher trust | Verify signed payload identity | Export cuts cannot waive privacy/security |
| Partners / CI integration | Notify partners; agree required paths | Implement agreed CI paths | **P0**<br>[#5344](https://github.com/Azure/azure-functions-core-tools/issues/5344) partner blockers<br>[#5424](https://github.com/Azure/azure-functions-core-tools/issues/5424) required CI paths | Prepared support handoff | Not every partner needs a tooling release |
| Migration / parity | Gather workflow/schema evidence | Agree alternatives and draft guidance | **P0**<br>[#5355](https://github.com/Azure/azure-functions-core-tools/issues/5355) parity<br>[#5356](https://github.com/Azure/azure-functions-core-tools/issues/5356) migration<br>[#5358](https://github.com/Azure/azure-functions-core-tools/issues/5358) config compatibility | Publish validated guidance | **Needs triage**<br>[#5340](https://github.com/Azure/azure-functions-core-tools/issues/5340)/[#5341](https://github.com/Azure/azure-functions-core-tools/issues/5341) Durable; [#5342](https://github.com/Azure/azure-functions-core-tools/issues/5342) pack<br>[#5345](https://github.com/Azure/azure-functions-core-tools/issues/5345) K8s/KEDA<br>[#5357](https://github.com/Azure/azure-functions-core-tools/issues/5357)/[#5359](https://github.com/Azure/azure-functions-core-tools/issues/5359) transition/support |
| Documentation | Inventory source contradictions | Update decision-dependent guidance | Required current docs accurate | Validated final-version notes | **Needs triage**<br>[#5350](https://github.com/Azure/azure-functions-core-tools/issues/5350) docs; [#5351](https://github.com/Azure/azure-functions-core-tools/issues/5351) help; [#5352](https://github.com/Azure/azure-functions-core-tools/issues/5352) hints<br>[#5348](https://github.com/Azure/azure-functions-core-tools/issues/5348) schemas<br>[#5361](https://github.com/Azure/azure-functions-core-tools/issues/5361)/[#5362](https://github.com/Azure/azure-functions-core-tools/issues/5362) localization/accessibility |
| Release / rollback | Draft release evidence/procedures | **P0**<br>[#5440](https://github.com/Azure/azure-functions-core-tools/issues/5440) promotion mechanism | **P0**<br>[#5423](https://github.com/Azure/azure-functions-core-tools/issues/5423) rehearsed rollback | **P0**<br>[#5363](https://github.com/Azure/azure-functions-core-tools/issues/5363) qualified cutover | Pipeline follow-ups need state/scope triage |
| Other feature breadth | No new feature implied | No issue committed without review | No planned feature development | Publication only | **Needs triage / after-GA candidates**<br>[#5343](https://github.com/Azure/azure-functions-core-tools/issues/5343) settings; [#5346](https://github.com/Azure/azure-functions-core-tools/issues/5346) repo migration<br>[#5347](https://github.com/Azure/azure-functions-core-tools/issues/5347) doctor; [#5380](https://github.com/Azure/azure-functions-core-tools/issues/5380) Aspire<br>[#5383](https://github.com/Azure/azure-functions-core-tools/issues/5383)/[#5384](https://github.com/Azure/azure-functions-core-tools/issues/5384) meta/setup |

RC1 columns above mean **acceptance/qualification complete**, not permission to
keep building those features during RC1. Finish selected feature implementation
in Preview 5. Start channels, signing/security, partners, migration and release
preparation before Preview 4 exits; do not wait for feature completion. No issue
is reserved for RC2; it exists only if blocking fixes require a replacement.

## Supporting PRs And Completed Predecessors

This context informs dependencies and options; it does not qualify a release.

| Area | Implementation context (October 6) | Placement implication |
| --- | --- | --- |
| Telemetry | PR [#5677](https://github.com/Azure/azure-functions-core-tools/pull/5677) was open at 1d015bc; earlier review at 3eeb7d7 does not certify that head | P1 consideration for the next agreed feature scope; policy/privacy acceptance before enabled inclusion |
| Templates | PR [#5670](https://github.com/Azure/azure-functions-core-tools/pull/5670) was a draft constraints design; current new still used the existing provider path | Team selects minimum migration or supported fallback; no automatic Preview 4 migration |
| Updater | PR [#5609](https://github.com/Azure/azure-functions-core-tools/pull/5609) merged; [#5610](https://github.com/Azure/azure-functions-core-tools/pull/5610) open non-draft; [#5611](https://github.com/Azure/azure-functions-core-tools/pull/5611)/[#5612](https://github.com/Azure/azure-functions-core-tools/pull/5612) drafts | Completed hardening is a predecessor, not proof #5333 is complete |
| Profiles / compatibility | PR [#5608](https://github.com/Azure/azure-functions-core-tools/pull/5608) open; [#5673](https://github.com/Azure/azure-functions-core-tools/pull/5673) draft | Minimum profile/compatibility scope remains a team decision |
| CDN migration | Issue [#5439](https://github.com/Azure/azure-functions-core-tools/issues/5439) observed closed; installer CDN selection and RID Host fixes already landed | Do not list this closed issue as active work; qualify actual released payloads separately |

# Issues Needing Input

An issue can appear here and in a proposed milestone: this view says what needs
attention now, not a second completion date. Input types are SCOPE, ARCHITECTURE,
PRIORITY, MILESTONE, OWNERSHIP, DEPENDENCY, ACCEPTANCE CRITERIA and CROSS-TEAM INPUT.

| Issue | Area | Input Needed | Why It Matters | Needed By | Proposed Next Step |
| --- | --- | --- | --- | --- | --- |
| [#5623](https://github.com/Azure/azure-functions-core-tools/issues/5623) | Updater/install | ACCEPTANCE CRITERIA | Preview-only install/update must not leave an advertised dead end | Before Preview 4 acceptance | Test the intended installer/manager path; separate full updater target |
| [#5624](https://github.com/Azure/azure-functions-core-tools/issues/5624) | Workloads | DEPENDENCY | Feed metadata alone does not prove clean-machine RID installability | Before Preview 4 acceptance | Identify exact supported payload/feed inputs and verify the path |
| [#5656](https://github.com/Azure/azure-functions-core-tools/issues/5656) | Quality/templates | ACCEPTANCE CRITERIA | CLI functional proof must not become exhaustive template-content testing | Before tests/migration work | Agree representative CLI scenarios and acceptance criteria |
| [#5353](https://github.com/Azure/azure-functions-core-tools/issues/5353) | Quality | SCOPE | Required CLI journeys and expensive cloud/content breadth have different cadence | Before Preview 5 implementation | Agree useful PR scenarios and separately qualified external flows |
| [#5354](https://github.com/Azure/azure-functions-core-tools/issues/5354) | Quality | ACCEPTANCE CRITERIA | No approved latency thresholds or generalized-framework commitment | Before RC1 qualification | Measure pinned builds now; agree minimum regression limits |
| [#5333](https://github.com/Azure/azure-functions-core-tools/issues/5333) | Updater | SCOPE | Full in-place update is a proposed target, not permission to waive recovery safety | Before Preview 5 scope | Retain and finish the stack, or approve a tested supported alternative |
| [#5349](https://github.com/Azure/azure-functions-core-tools/issues/5349), [#5522](https://github.com/Azure/azure-functions-core-tools/issues/5522) | Telemetry | SCOPE / ACCEPTANCE CRITERIA | Optional export, privacy and usable diagnostics are distinct outcomes | Before enabled inclusion or diagnostic changes | Decide exported content/opt-out acceptance and minimum diagnostics; review PR #5677's exact head separately |
| [#5655](https://github.com/Azure/azure-functions-core-tools/issues/5655), [#5629](https://github.com/Azure/azure-functions-core-tools/issues/5629), [#5633](https://github.com/Azure/azure-functions-core-tools/issues/5633) | Templates | ARCHITECTURE / DEPENDENCY | Migration may need acquisition/adapters and complete list/select/invoke behavior | Before milestone assignment | Confirm issue state and minimum retained migration versus supported fallback |
| [#5329](https://github.com/Azure/azure-functions-core-tools/issues/5329), [#5459](https://github.com/Azure/azure-functions-core-tools/issues/5459) | Profiles | ARCHITECTURE / OWNERSHIP | Safe reproducible release is required; full automation breadth is not settled | Before profile delivery | Agree minimum publisher/correction process and compatibility inputs |
| [#5419](https://github.com/Azure/azure-functions-core-tools/issues/5419) | Distribution | CROSS-TEAM INPUT / DEPENDENCY | Actual winget/MSI acceptance determines packaging work | Start now, before packaging | Obtain acceptance evidence, then choose eligible packaging |
| [#5414](https://github.com/Azure/azure-functions-core-tools/issues/5414), [#5415](https://github.com/Azure/azure-functions-core-tools/issues/5415) | Distribution | OWNERSHIP / ACCEPTANCE CRITERIA | Assignee metadata is not a publication date or validation result | Start now; finish RC1 | Confirm publisher plan, staged artifacts and both npm names |
| [#5416](https://github.com/Azure/azure-functions-core-tools/issues/5416), [#5421](https://github.com/Azure/azure-functions-core-tools/issues/5421) | Signing/quality | ACCEPTANCE CRITERIA / DEPENDENCY | Signing infrastructure is not every-artifact/notarization qualification | Start now; finish RC1 | Define advertised platform/stack/channel cells and trust evidence |
| [#5360](https://github.com/Azure/azure-functions-core-tools/issues/5360), [#5430](https://github.com/Azure/azure-functions-core-tools/issues/5430) | Security | OWNERSHIP / ARCHITECTURE | Review capacity and official/development-package trust policy need agreement | Start now, before dependent changes | Assign review input and select minimum verified trust/rejection contract |
| [#5344](https://github.com/Azure/azure-functions-core-tools/issues/5344), [#5424](https://github.com/Azure/azure-functions-core-tools/issues/5424) | Partners/CI | CROSS-TEAM INPUT / SCOPE | Actual adoption blockers, not every partner release, determine the required path | Intake now; close RC1 | Confirm required integrations and blocking feedback |
| [#5355](https://github.com/Azure/azure-functions-core-tools/issues/5355), [#5356](https://github.com/Azure/azure-functions-core-tools/issues/5356), [#5358](https://github.com/Azure/azure-functions-core-tools/issues/5358) | Migration/parity | SCOPE / DEPENDENCY | Final guidance depends on retained workflows and tested alternatives | Gather evidence now; close RC1 | Agree parity position and current configuration behavior |
| [#5340](https://github.com/Azure/azure-functions-core-tools/issues/5340), [#5341](https://github.com/Azure/azure-functions-core-tools/issues/5341), [#5342](https://github.com/Azure/azure-functions-core-tools/issues/5342) | Durable/pack | SCOPE / ACCEPTANCE CRITERIA | Functional cuts need demonstrated support/migration alternatives | Before any cut approval | Assess named workflows; do not equate a stub with parity |
| [#5440](https://github.com/Azure/azure-functions-core-tools/issues/5440), [#5423](https://github.com/Azure/azure-functions-core-tools/issues/5423) | Release | ACCEPTANCE CRITERIA / DEPENDENCY | Manifest promotion needs payload closure; rollback must use actual CDN selection | Design now; rehearse Preview 5 | Define gate results and executable staged correction paths |
| [#5363](https://github.com/Azure/azure-functions-core-tools/issues/5363) | Release | OWNERSHIP | Cutover/go-no-go is a human operation on qualified artifacts | Prepare before RC1; execute GA | Name approver/operator and final verification/handoff |
| [#5350](https://github.com/Azure/azure-functions-core-tools/issues/5350), [#5348](https://github.com/Azure/azure-functions-core-tools/issues/5348), [#5361](https://github.com/Azure/azure-functions-core-tools/issues/5361), [#5362](https://github.com/Azure/azure-functions-core-tools/issues/5362) | Docs/UX | SCOPE / PRIORITY | Current contracts/accessibility are required; broad rewrites, store registration and translations may differ | Before placement/scope approval | Confirm active state, retained obligations and optional breadth |

Keep the [CLI-functional test boundary](../v5-ga-readiness.md#cli-to-template-functional-tests)
for #5656 separate from broader Preview 3 regression tracking.

# Needs Triage

These leads are **not confirmed active work**. No release placement is proposed
until each item is validated. Candidate milestones below are options, not
placements. Confirm active state and remaining scope before treating a lead as
unfinished work.

| Issue / saved short description | Area | Known Context | Why Unresolved | Candidate Milestone | Input Required |
| --- | --- | --- | --- | --- | --- |
| [#4936](https://github.com/Azure/azure-functions-core-tools/issues/4936) schema-store lead | Documentation | Referenced with schema publication | State and required public-schema portion not verified | After GA for store convenience only | SCOPE |
| [#5319](https://github.com/Azure/azure-functions-core-tools/issues/5319) Java worker | Workloads | Original milestone inventory | May be completed; per-stack qualification is separate | TBD | Active state / support evidence |
| [#5320](https://github.com/Azure/azure-functions-core-tools/issues/5320) Java stack | Workloads | Original inventory | May be completed | TBD | Active state |
| [#5321](https://github.com/Azure/azure-functions-core-tools/issues/5321) PowerShell worker | Workloads | Original inventory | May be completed | TBD | Active state |
| [#5322](https://github.com/Azure/azure-functions-core-tools/issues/5322) PowerShell stack | Workloads | Original inventory | May be completed | TBD | Active state |
| [#5323](https://github.com/Azure/azure-functions-core-tools/issues/5323) PowerShell templates | Templates | Older remaining-work lead | Current delivery/closure not established | Preview 5 if required and still active | DEPENDENCY |
| [#5324](https://github.com/Azure/azure-functions-core-tools/issues/5324) quickstart extraction | Templates | Proposed architecture deferral | Retained-path dependencies and state unverified | After GA if current flow remains qualified | SCOPE |
| [#5325](https://github.com/Azure/azure-functions-core-tools/issues/5325) quickstart release pipeline | Templates/release | Follows extraction choice | Required supply versus optional relocation unclear | TBD | DEPENDENCY |
| [#5326](https://github.com/Azure/azure-functions-core-tools/issues/5326) RID suffix direction | Workloads | Original portable-package direction evolved | Could be superseded/completed | TBD | State / successor |
| [#5327](https://github.com/Azure/azure-functions-core-tools/issues/5327) Go templates | Templates | Original inventory | May be completed; advertised support still needs evidence | TBD | Active state |
| [#5328](https://github.com/Azure/azure-functions-core-tools/issues/5328) release design | Release | Release design exists | Parent closure/remaining scope not verified | TBD | State / remaining work |
| [#5330](https://github.com/Azure/azure-functions-core-tools/issues/5330) template engine extraction | Templates | Internal engine already partly implemented | Remaining user-facing work may be elsewhere | TBD | Scope / predecessor |
| [#5331](https://github.com/Azure/azure-functions-core-tools/issues/5331) dedicated vnext pipelines | Release | Pipeline infrastructure exists | Automation gaps versus completed parent unknown | TBD | Remaining delivery scope |
| [#5332](https://github.com/Azure/azure-functions-core-tools/issues/5332) profiles CDN | Workloads | Original release design | Current publication/owner evidence incomplete | Preview 5 if retained release requires it | DEPENDENCY |
| [#5340](https://github.com/Azure/azure-functions-core-tools/issues/5340) Durable workload | Migration/parity | Proposed feature cut | Cannot approve without affected-workflow/support evidence | After GA candidate | SCOPE / CROSS-TEAM INPUT |
| [#5341](https://github.com/Azure/azure-functions-core-tools/issues/5341) Durable CI/CD | Migration/parity | Depends on retained Durable scope | Current state and retained publishing needs unclear | After GA candidate | DEPENDENCY |
| [#5342](https://github.com/Azure/azure-functions-core-tools/issues/5342) pack/Azure CLI redesign | Migration/parity | Existing pack surface is not full parity | Supported packaging alternative must be tested before cut | After GA candidate, not an approved functional cut | ACCEPTANCE CRITERIA |
| [#5343](https://github.com/Azure/azure-functions-core-tools/issues/5343) settings story | Feature | Proposed new-surface deferral | Current schema compatibility must still pass | After GA candidate | SCOPE |
| [#5345](https://github.com/Azure/azure-functions-core-tools/issues/5345) Kubernetes/KEDA decision | Migration/parity | Original ship/drop decision | Usage, policy and active state unverified | Decide before RC1; implementation TBD | PRIORITY / SCOPE |
| [#5346](https://github.com/Azure/azure-functions-core-tools/issues/5346) owner-repository migration | Workloads | Proposed topology deferral | Current publisher responsibility must be explicit | After GA candidate | OWNERSHIP |
| [#5347](https://github.com/Azure/azure-functions-core-tools/issues/5347) func doctor | Feature | Proposed convenience deferral | Retained diagnostics/support needs must be checked | After GA candidate | SCOPE |
| [#5348](https://github.com/Azure/azure-functions-core-tools/issues/5348) schema publication | Documentation | Broader than store registration | Advertised schema URLs cannot be silently cut | After GA for optional registration only | SCOPE |
| [#5350](https://github.com/Azure/azure-functions-core-tools/issues/5350) docs epic | Documentation | Required guidance plus broader docs work | State and blocking subset not verified | Release-critical guidance before RC1 | SCOPE |
| [#5351](https://github.com/Azure/azure-functions-core-tools/issues/5351) help audit | Documentation | Original UX scope | Release-critical gaps versus wording polish unknown | TBD | PRIORITY |
| [#5352](https://github.com/Azure/azure-functions-core-tools/issues/5352) error next-step hints | Documentation | Original UX scope | Functional diagnostics versus broad polish unknown | TBD | ACCEPTANCE CRITERIA |
| [#5357](https://github.com/Azure/azure-functions-core-tools/issues/5357) v4 migration pointer | Migration | Original transition scope | Required user path versus optional code not decided | TBD | SCOPE |
| [#5359](https://github.com/Azure/azure-functions-core-tools/issues/5359) v4 support/deprecation | Migration | Maintenance direction exists | Exact EOL/security scope still requires input | Before RC1 guidance if still active | SCOPE |
| [#5361](https://github.com/Azure/azure-functions-core-tools/issues/5361) localization | Documentation | Ship/defer choice; not a translation commitment | Active state and supported language policy unknown | Choice before scope freeze | SCOPE |
| [#5362](https://github.com/Azure/azure-functions-core-tools/issues/5362) accessibility | Quality/docs | NO_COLOR/non-TTY work partly landed | Audit closure and remaining release blockers unknown | RC1 acceptance if still active | ACCEPTANCE CRITERIA |
| [#5380](https://github.com/Azure/azure-functions-core-tools/issues/5380) Aspire integration | Feature | Proposed convenience deferral | Partner expectations not established | After GA candidate | CROSS-TEAM INPUT |
| [#5383](https://github.com/Azure/azure-functions-core-tools/issues/5383) meta-package design | Workloads | Proposed refactor breadth | Hidden required setup dependency not ruled out | After GA candidate | ARCHITECTURE |
| [#5384](https://github.com/Azure/azure-functions-core-tools/issues/5384) meta/setup integration | Workloads | Follows meta-package choice | Required correctness must not be deferred | After GA candidate | DEPENDENCY |
| [#5459](https://github.com/Azure/azure-functions-core-tools/issues/5459) profile registry/release | Workloads | Related to #5329/design PR #5608 | State and minimum publisher/correction scope unknown | Preview 5 candidate | ARCHITECTURE / OWNERSHIP |
| [#5629](https://github.com/Azure/azure-functions-core-tools/issues/5629) template-package lifecycle | Templates | Referenced by ecosystem proposal | May contain a retained acquisition prerequisite | TBD until minimum migration is chosen | DEPENDENCY |
| [#5631](https://github.com/Azure/azure-functions-core-tools/issues/5631) template-package update | Templates | Lifecycle scope lead | Current state and retained-version needs unclear | After GA only for optional breadth | SCOPE |
| [#5632](https://github.com/Azure/azure-functions-core-tools/issues/5632) template-package uninstall | Templates | Lifecycle scope lead | Current state and safety/correction needs unclear | TBD | SCOPE |
| [#5636](https://github.com/Azure/azure-functions-core-tools/issues/5636) template-first init | Templates | Proposed ecosystem breadth | Minimum retained init UX not selected | TBD | ARCHITECTURE |
| [#5640](https://github.com/Azure/azure-functions-core-tools/issues/5640) quickstart workload work | Templates | Related to extraction | Exact current scope/title/state not captured | TBD | SCOPE |
| [#5651](https://github.com/Azure/azure-functions-core-tools/issues/5651) samples supply pipeline | Templates | Current proposed design exists | Delivery/GA scope not decided | After GA candidate | DEPENDENCY |
| [#5653](https://github.com/Azure/azure-functions-core-tools/issues/5653) template search/browse | Templates | Proposed discovery breadth | Active state and retained dependency not verified | After GA candidate | SCOPE |
| [#5655](https://github.com/Azure/azure-functions-core-tools/issues/5655) new catalog migration | Templates | Existing providers still control production new | Team must choose migration/fallback and confirm active work | Preview 5 candidate only, not committed | ARCHITECTURE / MILESTONE |
| [#5481](https://github.com/Azure/azure-functions-core-tools/issues/5481) PowerShell-template follow-up | Templates | Older remaining-work lead | Exact current title/state/scope not captured | TBD | Active state / DEPENDENCY |
| [#5445](https://github.com/Azure/azure-functions-core-tools/issues/5445) checksum follow-up | Updater | Mandatory-checksum hardening already landed | Verify closure or residual scope; do not duplicate completed implementation | TBD | State / residual acceptance |
| [#5268](https://github.com/Azure/azure-functions-core-tools/issues/5268) scaffolding known-failure reference | Quality | Mentioned in #5656 DoD | Current reproduction/closure not captured | TBD | ACCEPTANCE CRITERIA |
| [#5441](https://github.com/Azure/azure-functions-core-tools/issues/5441), [#5442](https://github.com/Azure/azure-functions-core-tools/issues/5442) CDN release follow-ups | Release | Earlier CLI release planning references | Exact titles, state and residual scope not captured | TBD | DEPENDENCY |
| [#5447](https://github.com/Azure/azure-functions-core-tools/issues/5447) TODO/FIXME sweep | Feature | Earlier hygiene proposal | No evidence that all sweep scope is GA-blocking | After GA for optional cleanup | PRIORITY |
| [#5561](https://github.com/Azure/azure-functions-core-tools/issues/5561) filesystem abstraction refactor | Quality | Earlier testability/refactor lead | Required test capability versus broad refactor unclear | After GA for optional refactor | SCOPE |
| [#5198](https://github.com/Azure/azure-functions-core-tools/issues/5198) workload search returns other package types | Workloads | Saved CLI bug lead | Reproduction and closure not established | Preview 4 candidate if still a supported-path bug | ACCEPTANCE CRITERIA |
| [#5283](https://github.com/Azure/azure-functions-core-tools/issues/5283) Python worker empty payload | Workloads | Saved packaging bug lead | Corrective packages may supersede it | TBD | Active state / DEPENDENCY |
| [#5286](https://github.com/Azure/azure-functions-core-tools/issues/5286) built-in profiles stable/prerelease mismatch | Workloads | Saved profile bug lead | Later profile changes may address it | TBD | Reproduction / DEPENDENCY |
| [#5310](https://github.com/Azure/azure-functions-core-tools/issues/5310) run stalls preparing project | Quality | Saved v5 runtime bug lead | Current reproduction/closure unknown | Preview 4 candidate if still reproducible | ACCEPTANCE CRITERIA |
| [#5369](https://github.com/Azure/azure-functions-core-tools/issues/5369) missing bundle/template fallback | Templates | Saved compatibility lead | Current policy and closure unknown | TBD | ARCHITECTURE |
| [#5370](https://github.com/Azure/azure-functions-core-tools/issues/5370) workload-search version/channel visibility | Workloads | Saved UX/catalog lead | Retained behavior and closure unknown | TBD | SCOPE |
| [#5448](https://github.com/Azure/azure-functions-core-tools/issues/5448) new-command decomposition | Templates | Saved refactor lead | Later changes may supersede it | After GA for optional refactor | Active state / PRIORITY |
| [#5451](https://github.com/Azure/azure-functions-core-tools/issues/5451) setup-runner decomposition | Workloads | Setup extraction already landed | Remaining scope/closure must be verified | TBD | State / residual work |
| [#5457](https://github.com/Azure/azure-functions-core-tools/issues/5457) TFM-aware template selection | Templates | Saved compatibility lead | Supported TFM policy and scope unknown | TBD | ARCHITECTURE |
| [#5577](https://github.com/Azure/azure-functions-core-tools/issues/5577) bundle resolution failure | Workloads | Saved v5 bug lead | Current reproduction/closure unknown | Preview 4 candidate if still active | ACCEPTANCE CRITERIA |
| [#5591](https://github.com/Azure/azure-functions-core-tools/issues/5591) Linux installer manifest failure | Distribution | Saved installer bug lead | Later installer fixes may address it | Preview 4 candidate if still active | Reproduction / active state |
| [#5633](https://github.com/Azure/azure-functions-core-tools/issues/5633) strict parameters/new runtime | Templates | Saved command-migration issue | Scope may be a retained migration prerequisite | Preview 5 candidate after scope agreement | ARCHITECTURE / DEPENDENCY |
| [#5676](https://github.com/Azure/azure-functions-core-tools/issues/5676) local.settings Key Vault references | Migration/parity | Saved v5 compatibility bug lead | Supported local behavior and closure unknown | TBD | SCOPE / ACCEPTANCE CRITERIA |

For unnumbered project-stack or shell-completion work, identify the actual tracker
and retained user outcome first; do not invent issue identities.

# Release Requirements That Need Clearer Tracking

**TRACKING GAP** means explicit tracking was not established in the inspected
evidence, not that no tracker exists anywhere. Agree the appropriate tracking
after review; do not invent issues or overload unrelated issues.

| Requirement | Why Required | Needed By | Related / Partial Issues | Proposed Action | Input Needed |
| --- | --- | --- | --- | --- | --- |
| **TRACKING GAP:** Preview 3 vs current CLI regression coverage | Explain meaningful changes using the same pinned inputs; do not invent baseline passes | First comparisons Preview 4; automated coverage Preview 5 | #5353; #5656 is separate functional scope | Agree explicit linked tracking and owner, not an automatic #5656 expansion | SCOPE / OWNERSHIP |
| **TRACKING GAP:** release readiness evidence | Bind source/version, signed artifacts, payload/profile inputs and test results before promotion | Every candidate, including exact final GA artifacts | #5440, #5421 | Identify existing evidence location or propose a focused child/new issue after review | ACCEPTANCE CRITERIA / OWNERSHIP |
| **TRACKING GAP:** workload/RID/profile release closure | All advertised combinations must acquire and run with compatible published inputs | Preview path before Preview 4 acceptance; complete RC1 | #5624, #5329, #5440; #5459 state unverified | Define exact acceptance and tracking without forcing it into an unrelated publisher task | ARCHITECTURE / DEPENDENCY |
| Tracked but underspecified: supported platform/stack/channel matrix | A green release must state what it supports | Before qualification; complete RC1 | #5421 | Clarify acceptance in the existing tracker after agreement | ACCEPTANCE CRITERIA |
| Tracked but underspecified: executable CDN-aware correction/playbook | Rehearse recovery without mutating shipped payloads or deleting workloads | Rehearse Preview 5; executable RC1 | #5423 | Agree minimum procedures and actual operator evidence | DEPENDENCY / OWNERSHIP |

Privacy has existing tracking in #5349 and security in #5360/#5430; they are not
new tracking gaps.
[Documentation readiness](../v5-ga-readiness.md#documentation-readiness) remains
a release requirement, separate from issue placement.

## Short Checkpoint Definitions

| Release | Meaning |
| --- | --- |
| Preview 3 | Shipped behavioral baseline, not a current feature-development bucket |
| Preview 4 | Reliability, plumbing, candidate quality and already-selected work |
| Preview 5 | Features the team agrees should land for GA; implementation complete before RC1 |
| RC1 | First candidate intended to be capable of shipping as GA; full required acceptance evidence |
| RC2, if required | Blocking fixes and revalidation only; no planned feature capacity |
| GA | Publish/cut over the exact qualified final artifacts |
| Post-GA / TBD | Explicit later-scope proposals or work needing responsible placement |

## Open Decisions

Use [decisions](decisions.md) for scope, template migration/fallback, compatibility,
quality ownership, winget, parity/support, capacity, telemetry, platform acceptance,
localization and Kubernetes/KEDA. The [entry guide](../v5-ga-readiness.md) owns
the protected acceptance requirements; these views do not waive them.
