# CLI v5 Templates — Engine Redesign Execution Plan

- **Date:** 2026-10-30
- **Status:** Phases 1–3 complete; Phase 3 accepted with follow-ups; Phase 4 next (not started). Architecture and implementation not approved.
- **Owner:** Francisco Gomez Gamino
- **Primary design starting point:** [Fabio Cavalcante's October 30 object-model proposal](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md)
- **Scope:** CLI v5 `func init` and `func new` templating engine and representative template qualification

> This document is a project execution checkpoint, not an approved product specification. Preserve the distinction between verified Git state, team direction, proposed contracts, and outstanding decisions. Promote stable design and behavior into authoritative documentation only after review.

## 1. Executive direction

Start from **Fabio's proposed object model**, not from the PR #5814-derived architecture. Build the smallest command-facing Templating Engine using existing Core Tools / Microsoft.TemplateEngine capabilities and a dedicated template hive where appropriate. Keep engine implementation separate from template packaging, publishing, and broad content migration. Prove discovery, metadata filtering, selection, parameter handling, and invocation with representative templates before expanding to supported language stacks.

The engine stays inside Core Tools for the current GA milestone; physical extraction tracked by issue #5330 is deferred until after GA. The team favors rapid, reviewable API-contract increments over waiting for a fully polished design. Release timing is subject to a quality-based go/no-go decision; no unconditional Ignite delivery commitment is made.

### Design authority and evidence hierarchy

The October 30 meeting with Fabio Cavalcante, Sarah Vu, and Francisco Gomez Gamino established Fabio's proposal as the starting architecture, not a final API contract. Do not reconcile it by merging competing historical architectures.

1. **Starting design:** [Fabio's object-model draft](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md). Its proposed names and signatures remain subject to review, not frozen public APIs.
2. **Supporting historical evidence:** [Architecture Reset](CLI-v5-Templates-Architecture-Reset-Clean.md), [Working Design](CLI-v5-Templates-Working-Design.md), historical research and behavioral baseline. Their conflicting proposals are not current requirements.
3. **Source behavior:** verified current `vnext`, reviewed PR #5820, and existing Core Tools / Microsoft.TemplateEngine contracts.
4. **Historical implementation:** PR #5814 and earlier P0 lanes are evidence only, not the implementation foundation.

## 2. Verified baseline and source provenance (October 30 checkpoint)

### Provisioning — Phase 1 complete

- Lane: `CLI-Templates-Engine-Redesign`
- Workspace: `Q:\Workspaces\agent-repos\CLI-Templates-Engine-Redesign\CLI-Templates-Engine-Redesign.code-workspace`
- Repository: `Q:\Workspaces\agent-repos\CLI-Templates-Engine-Redesign\azure-functions-core-tools`
- Provisioned detached HEAD: `c5361189f6e47a04207c7103fca7be29282bb6ff`
- Provisioned tree: `bd716f46a8cec99032d21955d85882e22555791c`
- Independent clone, clean working tree, zero untracked and ignored files at provisioning.
- **This is a bootstrap from the canonical repository's bug-bash branch, not a verified `vnext` or approved redesign baseline.**
- SBT/SMP (workspace provisioning) finished successfully; no product changes or integration occurred during provisioning.

### Clean redesign baseline — Phase 2 complete

- Verified upstream `vnext` HEAD: `9807f091f795388682b93efd2539aa6b3953de42`; tree: `50af74f966e3a9aca493b2ce4a3c0d5e50a9b017`.
- Development branch: `feature/templates-engine-redesign`, pointing directly to the reviewed PR #5820 head `5eb2bc4ef001762e0083cebe3a318066920d4050` (tree `74419dafdf6b527a664b81a0c4e0ff9f73af4fcd`). Verified `vnext` is a parent of that head; no redundant merge or cherry-pick was needed.
- The previous PR #5814-derived architecture branch was not merged. No product changes were added beyond the exact reviewed cleanup.
- Architecture Reset and Working Design were copied byte-for-byte from `templates/architecture-simplification` at `9d033c72548ead69f4e1177070e61e43e3be708f` (original source blobs `0d8d4b9bf18821d424b582cdd73374739d4e75b0` and `25cff3bef9085575b1dcfbfcccd7ddce3e0c8958`). Their historical notices are editorial additions to those copies. [Fabio's draft](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md) was supplied by Francisco as an attachment, not as a historical Git-tracked source; its new title and metadata do not change its proposed contracts.

### Fabio cleanup PR #5820 — reviewed evidence

- Review disposition: **ACCEPTABLE WITH FOLLOW-UPS** (static only).
- Reviewed base: `9807f091f795388682b93efd2539aa6b3953de42`
- Reviewed head: `5eb2bc4ef001762e0083cebe3a318066920d4050`
- Reviewed head tree: `74419dafdf6b527a664b81a0c4e0ff9f73af4fcd`
- At the October 30 baseline freshness check (approximately 19:34 PDT), open, not merged, with `mergeable_state=blocked`. No later live state is asserted.
- Removes legacy Templates.V2 / Templates.DotNet providers, orchestration, workload content, obsolete tests and packaging machinery; retains existing Microsoft.TemplateEngine integration.
- When the `func new` migration handler runs, creation and listing fail with a diagnostic and exit code 1 before target writes, including with `--force`. Interactive first-run setup can return success before reaching that handler (F5). Temporary unavailability is **not** the intended final v5 contract.
- `func init`, project adoption/healing, six stack initializers, quickstart, and runtime paths were not changed by the reviewed PR.
- The attempt to apply #5820 to `templates/architecture-simplification` was blocked by #5814-derived `IPublicPackageTemplates` wiring, obsolete tests/resources, and command-grammar divergence (`--output` versus `--format`). **No patch was applied.**
- Known pre-existing release-path issue in `eng/ci/release/official-release.workload.bundles.yml` remains a separate follow-up.
- **RUNTIME VALIDATION REQUIRED**; author-reported validation is not independently reproduced.

### Protected historical source

- `CLI-Templates-Simplification` branch: `templates/architecture-simplification`
- Verified HEAD: `9d033c72548ead69f4e1177070e61e43e3be708f`
- Verified tree: `75028d5805dd8952d54a178124b15d4c160f6ae5`
- Preserve this lane and all other existing Templates, research, review, qualification, and provisioning lanes without mutation.

## 3. Target architecture — starting model, not final API

Fabio's draft introduces a small command-facing model, including proposed concepts such as `ITemplaterFactory`, `TemplateEngineContext`, `Templater`, `TemplateCatalogEntry`, `TemplateGroup`, `ResolvedTemplate`, and parameter/invocation/result models. Validate whether each is necessary and whether existing Core Tools / Microsoft.TemplateEngine types can satisfy it.

### Retained infrastructure and missing command behavior

The retained Microsoft.TemplateEngine integration already provides factory and session creation, command-context host parameters, caller-owned disposable sessions, a dedicated shared template hive, installed-template discovery, catalog and package metadata, parameter projection, and constraint-validation and matching helpers.

Command-facing resolution and invocation are not complete. Relevant constraint components are not currently registered for the new command path. Existing infrastructure must not be mistaken for completed `func init` / `func new` integration; Phase 4 should identify the smallest missing behavior and avoid duplicating retained capabilities.

### Expected flow

1. Proposed, subject to Phase 4 confirmation: workload-delivered template packages would be registered in the dedicated CLI template hive. The current baseline does not implement this registration path.
2. Existing templater infrastructure discovers installed templates and exposes metadata; avoid duplicating acquisition or hive management.
3. CLI captures relevant command/project context, including an immutable project-input snapshot for the operation where required.
4. Shared selection filters templates by relevant language-stack/project metadata and preserves diagnostics for rejected candidates.
5. CLI maps command inputs to canonical template parameters, resolves one exact template identity, and invokes it.
6. Generation enforces explicit output/collision safety; CLI owns command orchestration and required configuration finalization.
7. Engine resources have clear command-scoped ownership and disposal.

**Terminology:** "Runtime" means Azure Functions app/Host runtime; "Stack" means language stack; "Generic Template" refers to the Template Engine concept. Do not overload these terms. Distinguish missing capabilities, Unsupported, and Unknown where compatibility matching requires them.

### Representative acceptance journeys

- The initial engine proof uses one representative item template to exercise discovery, multiple metadata values, filtering, exact selection, parameters, invocation, and safe output for `func new`.
- One representative project template exercises `func init` generation and required CLI finalization.
- These two templates are **not** the complete release acceptance scope. Fabio's draft proposes minimal project and item coverage across supported stacks as a release outcome. Broader Python, Node, and .NET workload migration follows the first engine proof; the exact GA inventory and acceptance matrix require team approval and runtime qualification.
- Preserve and disposition historical template IDs, prompting, compatibility, generated content, collisions, errors, and tests without assuming every historical behavior is GA-required.

## 4. Execution phases and gates

| Phase | Owner / lane | Deliverable | Status | Exit gate |
|---|---|---|---|---|
| **1. Provision lane** | SBT/SMP | Isolated `CLI-Templates-Engine-Redesign` workspace and manifest | **COMPLETE** | Independent clone and preservation verified |
| **2. Clean baseline** | Engine Redesign (Senior) | Verified `vnext` + reviewed #5820 cleanup; exact architecture sources; `feature/templates-engine-redesign` | **COMPLETE** | Branch at reviewed PR head; no #5814 implementation or engine changes |
| **3. Baseline inspection** | Independent Principal session in `CLI-Templates-Engine-Redesign` | Git provenance, cleanup scope, excluded #5814 code, doc preservation, risk/gap report | **COMPLETE — ACCEPTED WITH FOLLOW-UPS** | Baseline accepted for subsequent design; review findings carried forward |
| **4. Fabio-first object-model proposal** | Engine Redesign (Senior) | Minimal contracts and `func init` / `func new` flows grounded in Fabio's draft | **NEXT — NOT STARTED** | No open-ended re-architecture; preserve known requirements and unresolved questions |
| **5. Independent architecture review** | Independent Principal | Read-only challenge of API simplicity, ownership, reuse, behavior, tests, and documentation | **PENDING** | Findings and required changes recorded |
| **6. Semifinal architecture and readiness** | Francisco + Copilot | Reconcile findings; select semifinal architecture; identify bounded implementation/qualification lanes, dependencies and milestones | **PENDING** | Design-review candidate; proposed lane allocation, not premature provisioning |
| **7. Team design review** | Fabio + Sarah + Francisco | Approval or specific follow-ups; confirm Fabio's scaffolding ownership and Sarah's workload/content ownership | **PENDING** | **APPROVED**, **APPROVED WITH FOLLOW-UPS**, or **REVISION REQUIRED** |
| **8. Incremental development** | Approved development owners | A: API scaffolding; B: representative `func new`; C: `func init`; D: broader stack/content enablement | **PENDING** | Focused tests and reviewable changes; avoid overlapping lane ownership |
| **9. Independent implementation review** | Independent Principal | .NET/C# simplicity, API quality, reuse, adversarial tests, docs/spec fidelity, diff hygiene | **PENDING** | Findings reconciled before publication/merge |
| **10. Qualification and release decision** | Qualification owner + team | Restore/build/test/pack/publish; all-stack behavior; installed builds/RIDs; workload and packaging smoke tests | **PENDING** | Runtime evidence and quality-based go/no-go |

Phase 5 reviews **architecture**, not implementation. Phase 9 reviews **implemented code**. Independent reviews are inspection-only; executable validation belongs to a separately authorized validator.

## 5. Phase 3 — Independent Principal Baseline Review

**Disposition: ACCEPTED WITH FOLLOW-UPS.** Bounded static inspection found no verified product defect introduced by the cleanup. Runtime validation was not performed; acceptance authorizes subsequent architecture work, not release or implementation approval.

**Reviewer provenance:** The logical role was Independent Principal Software Engineer / Architect. The physical GitHub Copilot CLI session, `Independent Principal Baseline Review - 10-30`, ran in the `CLI-Templates-Engine-Redesign` physical workspace. Its independent inspection copy used verified Git objects and preserved document hashes. It did **not** run in the physical `CLI-Templates-Architecture-Review` workspace; that lane remains available for later independent architecture review.

| Finding | Severity / origin | Observation | Disposition |
|---|---|---|---|
| **F1 — Superseded historical architecture** | **HIGH — documentation discrepancy** | The historical Working Design proposes competing object models, physical engine-extraction gates, replacement-before-retirement sequencing, and broader template-content assumptions that conflict with the October 30 direction. | Treat these as historical proposals, not requirements; do not rewrite the preserved documents or import their architecture. |
| **F2 — Command grammar** | **MEDIUM — command-contract discrepancy** | Current `func new` uses `--format`, while the historical Working Design specifies `--output`. Other historical proposals include positional-template changes, `--path`, dry-run, and upward project discovery. | Preserve current grammar; any change requires an explicit design decision. |
| **F3 — Validation before destructive effects** | **HIGH — verified pre-existing defect** | Forced initialization can clear the target before validating some .NET framework inputs (`src/Func/Commands/InitCommand.cs`, `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs`). | Carry validation-before-destructive-effects into the next architecture and behavioral review; do not change code at this checkpoint. |
| **F4 — Required finalization failures** | **MEDIUM — verified pre-existing defect** | Current initialization can report configuration persistence failure as a warning while returning success. | Define required finalization failure and file-effect reporting in the future command contract; do not change code now. |
| **F5 — First-run behavior** | **MEDIUM — inherited behavior / qualification gap** | Interactive first-run setup can return success before reaching the `func new` migration handler. | Do not claim unconditional exit code 1; qualify this scenario and explicitly disposition it in design. |
| **F6 — Historical evidence availability** | **LOW — documentation provenance discrepancy** | Some historical research paths cited in the Working Design are absent from the new checkout. | Treat them as historical source-workspace evidence; do not recreate the research files. |

## 6. Phase 4 entry criteria and next design transaction

The accepted baseline, available Fabio draft, preserved historical documents, identified retained engine capabilities, and explicit dispositions for F1–F6 permit Phase 4 to begin as a separately authorized **design-only** transaction. Phase 4 is **next, not started**.

The Phase 4 proposal must cover:

- The minimal object model, responsibilities and lifetimes, and reuse-versus-new functionality assessment.
- End-to-end `func new` and `func init` flows, including command ownership.
- Template metadata, discovery, filtering and selection; parameter matching and invocation contracts.
- The open group-versus-candidate selection question, including how a chosen catalog entry retains an exact variant; no policy is approved here.
- Filesystem collision safety, known file effects, and required CLI finalization behavior.
- A disposition matrix for relevant historical behaviors, including first-run behavior, and open questions for Fabio and Sarah.

Do not begin API scaffolding, object-model reconciliation, or implementation in this documentation checkpoint. Independent architecture review, team design review, implementation review, and runtime qualification remain separate gates.

## 7. Proposed implementation ownership (to finalize in Phase 6/7)

| Responsibility | Proposed lane / owner | Dependency |
|---|---|---|
| Shared engine and small object model | Engine Redesign | Team-reviewed contracts |
| `func init` / `func new` command seam | Bounded integration lane, only if independent | Stable engine contract |
| Representative template and workload migration | Sarah/content owner to confirm | Template metadata/format and hive contract |
| Independent architecture and code review | Architecture Review or separate approved Principal reviewer | Exact immutable candidate |
| Runtime/behavioral qualification | Dedicated authorized qualification lane | Buildable implementation and fixtures |

Do not provision all proposed lanes now. Decide ownership and non-overlapping file boundaries in Phase 6, confirm at team review, then allocate only necessary lanes. Fabio may provide initial scaffolding; confirm before assigning duplicate work.

## 8. Quality, safety, and evidence gates

- Prefer minimal idiomatic .NET/C# and reuse of existing utilities; minimize public API and unnecessary generalization.
- Inspect tests/CI statically during review; no builds, tests, dynamic compilation, custom probes, reflection execution, symlink experiments, or downloaded-source execution under read-only reviewer authority.
- Independent reviewers use isolated copies with exact Git source/target objects and verify references/diffs before review.
- Record **RUNTIME VALIDATION REQUIRED** for unexecuted validation. Authorized qualification should eventually cover restore, Release build/test/pack/publish, all-stack init/adoption/healing/force, `func new` safety, quickstart/setup/workloads/runtime, bundle-channel packaging, and all-RID installed builds.
- No reset, clean, stash, deletion, force branch overwrite, or modification of canonical/historical lanes. Preserve untracked and ignored artifacts.
- No GitHub login, account switching, token refresh, SSO or credential changes. Preserve `Francisco-Gamino`; do not activate `frangom_microsoft`.
- Francisco owns commits, pushes, PR publication, and merges; agent output should be review-ready and clearly distinguish uncommitted from published state.
- Maintain exact provenance for commits, trees, blobs, documents, design decisions, and source availability. Avoid re-running historical research without a concrete gap.

## 9. Open decisions and dependencies

Resolved prerequisites: the verified `vnext` commit matches PR #5820's reviewed base, and Francisco's attached Fabio draft is preserved in the redesign workspace. These are provenance conclusions, not approval of proposed APIs.

1. **Fabio's implementation role:** confirm whether he will supply initial object-model scaffolding.
2. **Sarah's content handoff:** confirm workload repository links, format/migration ownership, and representative fixture requirements.
3. **Minimal GA scope:** disposition historical behavior against agreed quality and release scope, not automatic parity with obsolete implementation.
4. **Reviewable API contract:** determine which proposed objects are essential; avoid competing abstractions and preserve established terminology.
5. **Package/hive behavior:** assess retained Microsoft.TemplateEngine facilities and the missing constraint registration before introducing custom discovery or metadata mechanisms.
6. **Validation and file effects:** decide how the command contracts address F3–F5, including pre-generation validation, required finalization failures, partial writes, and first-run behavior.
7. **Qualification owner and matrix:** agree on OS/RID, stacks, installed package and collision scenarios before claiming readiness.
8. **Release go/no-go:** Fabio/Anirudh quality-based decision expected following October 30 discussion; no date or outcome assumed.
9. **Pre-existing release-path issue:** track separately from redesign baseline unless it becomes a qualification blocker.

## 10. Documentation and repository integration strategy

This plan is added to the accepted redesign baseline for Francisco's review. It records status and Principal findings but remains an execution plan, not an authoritative product specification.

Repository path:

`docs/templates/CLI-v5-Templates-Engine-Redesign-Execution-Plan-2026-10-30.md`

**Documentation checkpoint prepared October 30, 2026.** Four documents are present in the redesign repository: this plan, [Fabio's draft](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md), [Architecture Reset](CLI-v5-Templates-Architecture-Reset-Clean.md), and [Working Design](CLI-v5-Templates-Working-Design.md). All remain untracked, unstaged, and uncommitted pending Francisco's approval. No product code was changed during the checkpoint. The historical notices and the draft's title and metadata are editorial additions; preserve their original technical proposals for reference.

No product changes, commits, pushes, or PR publication are part of this documentation checkpoint.

## 11. Next checkpoint

The next development transaction is **Phase 4: Fabio-first object-model reconciliation and design proposal**, using the preserved draft, retained engine capabilities, historical behavior evidence, and F1–F6 dispositions. Begin only when separately authorized; do not scaffold or implement APIs in this checkpoint. The independent architecture and team review gates remain pending before implementation approval.
