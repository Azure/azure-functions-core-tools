# CLI v5 Templates — Engine Redesign Execution Plan

> **Historical planning record (October 30, 2026), not the current implementation task list.** This trimmed record keeps the lasting design decisions from that checkpoint; workspace provisioning, phase scheduling, review choreography and handoff details were removed. See [Templates Architecture](../../README.md) for current scope and status. This record does not authorize implementation.

- **Date:** 2026-10-30
- **Primary design starting point:** [Fabio Cavalcante's October 30 object-model proposal](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md)
- **Scope:** CLI v5 `func init` and `func new` templating engine and representative template qualification

## 1. Executive direction

Start from **Fabio's proposed object model**, not from the PR #5814-derived architecture. Build the smallest command-facing Templating Engine using existing Core Tools / Microsoft.TemplateEngine capabilities and a dedicated template hive where appropriate. Keep engine implementation separate from template packaging, publishing, and broad content migration. Prove discovery, metadata filtering, selection, parameter handling, and invocation with representative templates before expanding to supported language stacks.

The engine stays inside Core Tools for the current GA milestone; physical extraction tracked by issue #5330 is deferred until after GA. The team favors rapid, reviewable API-contract increments over waiting for a fully polished design. Release timing is subject to a quality-based go/no-go decision; no unconditional Ignite delivery commitment is made.

### Design authority and evidence hierarchy

The October 30 meeting with Fabio Cavalcante, Sarah Vu, and Francisco Gomez Gamino established Fabio's proposal as the starting architecture, not a final API contract. Do not reconcile it by merging competing historical architectures.

1. **Starting design:** [Fabio's object-model draft](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md). Its proposed names and signatures remain subject to review, not frozen public APIs.
2. **Supporting historical evidence:** [Architecture Reset](CLI-v5-Templates-Architecture-Reset-Clean.md), [Working Design](CLI-v5-Templates-Working-Design.md), historical research and behavioral baseline. Their conflicting proposals are not current requirements.
3. **Source behavior:** `vnext`, reviewed PR #5820, and existing Core Tools / Microsoft.TemplateEngine contracts.
4. **Historical implementation:** PR #5814 and earlier P0 work are evidence only, not the implementation foundation.

## 2. Cleanup baseline (PR #5820)

- Removes legacy Templates.V2 / Templates.DotNet providers, orchestration, workload content, obsolete tests and packaging machinery; retains existing Microsoft.TemplateEngine integration.
- When the `func new` migration handler runs, creation and listing fail with a diagnostic and exit code 1 before target writes, including with `--force`. Interactive first-run setup can return success before reaching that handler (F5). Temporary unavailability is **not** the intended final v5 contract.
- `func init`, project adoption/healing, the stack initializers, quickstart, and runtime paths were not changed by the cleanup.

## 3. Target architecture — starting model, not final API

Fabio's draft introduces a small command-facing model, including proposed concepts such as `ITemplaterFactory`, `TemplateEngineContext`, `Templater`, `TemplateCatalogEntry`, `TemplateGroup`, `ResolvedTemplate`, and parameter/invocation/result models. Validate whether each is necessary and whether existing Core Tools / Microsoft.TemplateEngine types can satisfy it.

### Retained infrastructure and missing command behavior

The retained Microsoft.TemplateEngine integration already provides factory and session creation, command-context host parameters, caller-owned disposable sessions, a dedicated shared template hive, installed-template discovery, catalog and package metadata, parameter projection, and constraint-validation and matching helpers.

Command-facing resolution and invocation are not complete. Relevant constraint components are not currently registered for the new command path. Existing infrastructure must not be mistaken for completed `func init` / `func new` integration; identify the smallest missing behavior and avoid duplicating retained capabilities.

### Expected flow

1. Proposed, subject to design confirmation: workload-delivered template packages would be registered in the dedicated CLI template hive. The baseline does not implement this registration path.
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

## 4. Baseline review findings

A bounded static review accepted the cleanup baseline with follow-ups. Runtime validation was not performed.

| Finding | Severity / origin | Observation | Disposition |
|---|---|---|---|
| **F1 — Superseded historical architecture** | **HIGH — documentation discrepancy** | The historical Working Design proposes competing object models, physical engine-extraction gates, replacement-before-retirement sequencing, and broader template-content assumptions that conflict with the October 30 direction. | Treat these as historical proposals, not requirements; do not rewrite the preserved documents or import their architecture. |
| **F2 — Command grammar** | **MEDIUM — command-contract discrepancy** | Current `func new` uses `--format`, while the historical Working Design specifies `--output`. Other historical proposals include positional-template changes, `--path`, dry-run, and upward project discovery. | Preserve current grammar; any change requires an explicit design decision. |
| **F3 — Validation before destructive effects** | **HIGH — verified pre-existing defect** | Forced initialization can clear the target before validating some .NET framework inputs (`src/Func/Commands/InitCommand.cs`, `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs`). | Require validation before destructive effects in the command contract. |
| **F4 — Required finalization failures** | **MEDIUM — verified pre-existing defect** | Current initialization can report configuration persistence failure as a warning while returning success. | Define required finalization failure and file-effect reporting in the command contract. |
| **F5 — First-run behavior** | **MEDIUM — inherited behavior / qualification gap** | Interactive first-run setup can return success before reaching the `func new` migration handler. | Do not claim unconditional exit code 1; qualify this scenario and explicitly disposition it in design. |
| **F6 — Historical evidence availability** | **LOW — documentation provenance discrepancy** | Some historical research paths cited in the Working Design are absent from this checkout. | Treat them as historical evidence; do not recreate the research files. |

## 5. Quality principles

- Prefer minimal idiomatic .NET/C# and reuse of existing utilities; minimize public API and unnecessary generalization.
- Record **RUNTIME VALIDATION REQUIRED** for unexecuted validation. Qualification should eventually cover restore, Release build/test/pack/publish, all-stack init/adoption/healing/force, `func new` safety, quickstart/setup/workloads/runtime, bundle-channel packaging, and all-RID installed builds.

## 6. Open decisions at the checkpoint

1. **Content handoff:** workload repository links, format/migration ownership, and representative fixture requirements.
2. **Minimal GA scope:** disposition historical behavior against agreed quality and release scope, not automatic parity with obsolete implementation.
3. **Reviewable API contract:** determine which proposed objects are essential; avoid competing abstractions and preserve established terminology.
4. **Package/hive behavior:** assess retained Microsoft.TemplateEngine facilities and the missing constraint registration before introducing custom discovery or metadata mechanisms.
5. **Validation and file effects:** decide how the command contracts address F3–F5, including pre-generation validation, required finalization failures, partial writes, and first-run behavior.
6. **Qualification matrix:** agree on OS/RID, stacks, installed package and collision scenarios before claiming readiness.
7. **Release go/no-go:** quality-based decision; no date or outcome assumed.
8. **Pre-existing release-path issue:** `eng/ci/release/official-release.workload.bundles.yml` is tracked separately unless it becomes a qualification blocker.
