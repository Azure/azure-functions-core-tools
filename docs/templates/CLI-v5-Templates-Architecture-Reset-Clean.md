# CLI v5 Templates — Architecture Reset

> **Historical architecture-reset charter — not current implementation authority.** This document preserves the investigation and simplification objectives established before the October 30, 2026 Templates discussion. Its architecture, process-lifetime inventory, bundled-content assumptions, and implementation sequence remain historical proposals. The current redesign starts from [Fabio's object-model draft](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md) and the reviewed PR #5820 cleanup baseline. See the [Engine Redesign Execution Plan](CLI-v5-Templates-Engine-Redesign-Execution-Plan-2026-10-30.md) for current decisions and review gates.

**Status:** Working design and investigation charter; not an approved implementation specification

**Date:** October 29, 2026

**Target:** Isolated simplification working branch; promote approved decisions into repository specs before implementation

## Purpose

> We will design the smallest implementation that preserves all supported behavior, makes `func init` and `func new` understandable end to end, and leaves room for Quickstart and future templates without implementing unnecessary infrastructure.

The current Templates PR introduced substantial production code, tests, and files. The team requested a step back to verify that each component is needed, simplify the object model, clarify naming and ownership, and show the complete command flows. The new Templates implementation is excluded from the upcoming bug bash. This reset does **not** assume that existing implementation contracts or historical code are the desired final architecture.

**Priorities:** (1) minimize implementation complexity and churn while meeting requirements; (2) produce accurate end-to-end `func init` and `func new` flow diagrams. Preserve existing command behavior and safety as non-negotiable acceptance criteria.

## Design principles

- Start with the existing supported CLI user experience and original design intent, not with classes already implemented.
- Reuse existing Core Tools mechanisms when they fit. Every retained abstraction needs a concrete responsibility, consumer, and reason it cannot be simpler.
- Keep the solution **modular**: command orchestration belongs in the CLI; reusable template discovery, selection, parameter processing, and generation should have a clear component boundary. Evaluate work item **#5330 — refactor the template engine out of the core CLI**. Do not force premature extraction or create a large interface hierarchy merely to anticipate it.
- Keep language-stack-specific project inspection and semantics out of the shared engine. Template metadata should express applicable stack, language/programming model, template kind, requirements, parameters, and stable identity as needed. Stack-specific implementations interpret project context; shared matching should not own capability definitions.
- Separate template **authoring/production**, **acquisition/installation**, **local discovery**, and **generation**. The consumer must not depend on whether content came from the Templates repository, Quickstart, or a connector monorepo.
- Preserve established Azure Functions meanings for *Runtime* (Functions Host/app runtime), *Stack* (language stack), and *Generic Template* (template engine). Avoid ambiguous `native` names and duplicate representations such as candidate/descriptor/catalog/provider without demonstrated needs.
- Do not change public `func init`/`func new` syntax to expose internal package coordinates. Do not promise atomic all-or-nothing publication unless proven.
- Quickstart, connectors, and other future template categories must fit the model conceptually; their full command workflows, remote feeds, and package lifecycle are **not** initial implementation scope.

## Historical evidence and baseline

1. **Original design:** Recover original Templates documents and pre-edit revisions from the August 2026 design history, especially #5533 (package lifecycle), #5534 (`func init`), #5535 (`func new`), #5555 (samples pipeline), and related Quickstart design. Distinguish original text from later modifications.
2. **Behavioral reference:** `vnext` as of September 16, 2026, reconstructed first-parent revision `9c41fa2e84d057d00c796332708e3f6e17472784`. This is a historical reference, not automatically the future implementation base; reconcile later fixes.
3. **Historical implementations:** `integration/templating-updates` at `04fd6d1e8e8fe34cac80be25996cc7ad086df884`; the later Templates stack (including #5752); historical command integration #5731. Record exact provenance and avoid assuming they work end to end.
4. **Current candidate:** PR #5814 at `1970fe07162d9ce2efdf79dd8c96f25e3e7317b3`, base `664830e683a35dffb9747bbfcac48929c591451c`. Preserve as implementation evidence, not architectural authority.

All historical research must use pinned local refs and report verified behavior, documented intent, proposals, and runtime-validation gaps separately.

## Initial template availability and discovery — proposed design

- Ship the template content currently used by supported v4 `func init` and `func new` experiences. First inventory actual sources, packaging, formats, and coverage across stacks; do not assume a single source package.
- Select one deterministic, CLI-resolvable **bundled-template location**. Prefer a versioned, installation-relative, read-only layout, subject to verification across actual distribution formats and operating systems. The exact directory name/layout is a pending design decision.
- Initialize discovery **when a Templates command needs it**, not at unrelated CLI startup. Enumerate available template identities and metadata, and cache the inventory **for the process lifetime**. Load selected template content on demand. Since ordinary CLI invocations are separate processes, this cache is not cross-command persistence. Do not implement a persistent cache, watchers, or background refresh without measured need.
- `func new` obtains the discovered inventory, checks applicable project/stack compatibility before template-specific prompting, selects the requested template, resolves parameters, renders, and safely writes output. `func init` uses the same source of available templates where appropriate but retains its distinct project-creation behavior.
- Future installed packages may use a separate user-writable location and feed the same discovery contract. Package install/update/uninstall, remote acquisition, and browse/search are deferred. Do not introduce unused providers or package-management machinery now.
- Define behavior for missing/corrupt bundled content and cache lifetime; preserve appropriate package/content integrity checks at the trust boundary and when consuming potentially mutable content.

## Template identity and naming — design questions

Distinguish **stable template identity**, **user-facing display name/aliases**, **template kind** (project/item/Quickstart/other), **stack/language/programming model applicability**, and **package identity/version**. Reuse established v4 names/aliases where possible. Avoid encoding every attribute redundantly in identifiers. Audit file names, classes, interfaces, namespaces, and docs for clear responsibilities; do not create a centralized naming or capability-governance system without evidence.

## Cross-stack behavioral baseline and regression gate

Before implementation, inventory **every stack supported by the September 16 v4 baseline**; determine the exact matrix from source and tests rather than guessing. For each stack, capture:

- `func init` variants, options/aliases/defaults, prompts, noninteractive use, generated files, settings, existing-project behavior, `--force`, and errors.
- `func new` template/trigger inventory, options and template-specific parameters, prompts, aliases, compatibility behavior, generated files, collisions, `--force`, and errors.
- Relevant Quickstart behavior as a historical/future design input, without committing to its implementation now.
- Existing tests, missing tests, and representative CLI journeys. Capture expected exit status, output, and filesystem effects; compare generated content semantically when nondeterminism exists.
- Later legitimate fixes and approved behavioral differences.

Produce a **stack × command × scenario × baseline evidence × replacement coverage × validation status** matrix. Static inspection is not a passing runtime test. Run controlled baseline/replacement regression and E2E scenarios only in a separately authorized validation transaction. No supported option, template, or stack behavior may be dropped silently.

## Python and connector/monorepo consolidation

Earlier investigation identified Python Quickstart HTTP `.nupkg` producer evidence in Azure Functions Templates PRs **#1823** (manual Quickstart packager) and **#1824** (Func-host metadata). These were **manual producer tooling**, not proof of production pipeline, signing, feed publishing, CLI distribution, or general Python `func init`/`func new` parity. Reconcile the original Python/Quickstart package lineage and metadata with the proposed common discovery/consumption model. Do not automatically adopt the handwritten package writer or its architecture.

A separate prior design discussion identified the need for **connector samples stored in folders inside a monorepo**. The producer/onboarding process must be able to identify and package selected content from a repository subfolder; source-repository layout knowledge must not leak into the CLI or template engine. Revisit the original YAML authoring/seeding design and decide the smallest producer-side mechanism when that capability is implemented. Search/browse remains deferred.

**Python design acceptance:** The model must represent Python project and item templates and Python Quickstart packages without adding Python-specific branches to the shared generation pipeline. Verify programming-model metadata, project inspection, compatibility, parameter contracts, and actual v4 behavior before freezing the schema. These are design requirements, not claims of completed implementation.

## Required design outputs

1. **Two end-to-end sequence diagrams** for `func init` and `func new`: user interaction, CLI entry, lazy inventory, project inspection, eligibility, selection, parameters, template content, rendering, collision handling, publication, and failures. Show concrete handoffs and object lifetimes.
2. **Small object model and responsibility table:** CLI versus reusable engine versus stack-specific code versus content producer; identify extraction seam for #5330.
3. **User experience and compatibility matrix:** preserve v4 public behavior, including all supported stacks and template-specific options; distinguish current UX from proposed improvements.
4. **Minimal local package/discovery and naming decisions:** paths, metadata, identities, cache lifetime, integrity boundaries, future installed content.
5. **Simplification inventory:** compare historical implementations and #5814; classify KEEP / SIMPLIFY / REUSE / MERGE / REMOVE / DEFER. Separate production-code, test, docs, and template-asset churn. Record missing functionality explicitly; no unexplained gaps.
6. **Short decision register:** evidence, unresolved question, proposed minimal answer, approval state, and impact. The team resolves undefined product behavior; agents must not silently invent requirements.

## Phases and approval gates

1. **Historical design reconstruction (read-only):** Recover original docs, later edits, and requirements/gaps.
2. **All-stack behavioral reconstruction (read-only):** Establish v4 baseline and test inventory; plan separately authorized runtime qualification.
3. **Joint design-gap resolution:** Review original design, decide missing requirements, redefine the complete user experience, and challenge naming and modularity.
4. **Minimal design:** Produce both diagrams, small object model, naming/discovery contracts, Python/Quickstart/connector extension checks, and implementation reduction plan.
5. **Team design review — mandatory before implementation:** Review and reconcile the diagrams, behavior matrix, and every retained major abstraction. Record decisions and blockers; no product implementation before design approval.
6. **Implementation:** Choose a fresh approved base after target-branch freshness verification; simplify or replace #5814 based on evidence. Preserve historical worktrees. Remove obsolete parallel paths only with parity protection.
7. **Qualification and independent review:** All-stack regression comparison, focused adversarial tests, platform/installed-build validation, .NET quality, naming/editorial review, and independent inspection-only Principal review. Distinguish runtime gaps from passes.
8. **Documentation promotion:** Turn approved design and observed implementation behavior into authoritative repository specs/docs; preserve historical provenance and remove superseded claims.

## Current workspace and preservation boundaries

The three independent lanes are provisioned at pinned PR #5814 head `1970fe07162d9ce2efdf79dd8c96f25e3e7317b3`:

- `CLI-Templates-Historical-Research` — read-only research.
- `CLI-Templates-Architecture-Review` — independent read-only review.
- `CLI-Templates-Simplification` — standby; no implementation authority yet.

The working design document may be added to a dedicated branch in the Simplification lane after live provenance verification; that does not authorize product implementation. Preserve existing PRs, worktrees, dirty files, manifests, and authentication. Do not reset, clean, stash, fetch for freshness, change credentials, build, test, commit, push, or publish without explicit transaction-specific authority. Historical research and Principal review remain inspection-only.

## Immediate next steps

1. Verify the Simplification lane and create a **design-only branch** under separate authorization; add this cleaned charter. Do not start implementation.
2. Start read-only original-design and all-stack baseline reconstruction in Historical Research using pinned refs.
3. Bring unresolved requirements back for team decisions; draft the complete UX and minimal diagrams.
4. Run independent architecture challenge, then the team design review.
5. Activate implementation only after the reviewed design is approved.

**Success criterion:** A new contributor can understand the supported `func init` and `func new` experience and the minimal modular architecture from two diagrams and a small object model, without reconstructing the historical PR stacks. Every retained abstraction is necessary, every supported baseline behavior has regression coverage, and future Quickstart, Python, and connector content can fit without rebuilding the core engine.
