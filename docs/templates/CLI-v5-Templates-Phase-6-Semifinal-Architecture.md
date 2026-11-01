# Templates Architecture

Status: Proposed — pending team approval.  
Date: October 31, 2026.

This document describes the proposed architecture for template-based `func init` and `func new` commands.

Product implementation and release require separate approval. Source identities and review evidence are preserved in [Provenance and review history](#provenance-and-review-history).

## Decision and evidence

The design reuses the existing Microsoft.TemplateEngine-based Template Engine inside Core Tools. CLI commands own input, prompts, Stack-specific interpretation, destination authorization, publication, required project configuration and final command status.

The command-scoped Templater owns installed discovery, eligibility, an exact session-bound selection and private Template Engine invocation. Installers and producers own package delivery and registration. Physical extraction under #5330 remains after GA.

The smallest proof is a real project template through `func init`, followed by a real **parameterized function** template through `func new`, ending with a discoverable generated function. This proves Template Engine and command integration, not release parity across supported Stacks. The existing text-file fixture proves installation/listing only.

Current code has a synchronous factory/session and `Templater.ListAsync`, but **no command-facing resolve/invoke**, no production registration of workload-delivered templates and no active `func new` generation. A design requirement is not a claim of implemented behavior or an authorization to change the CLI.

<a id="normative-authority"></a>

## Document status and requirements

The [Templates Architecture README](README.md) is the single entry point and reading order. **This Phase 6 document alone owns the proposed initial implementation requirements**, subject to team decisions; it does not approve a new public CLI contract.

**Current behavior** is evidenced by `src\Func\Commands\NewCommand.cs`, `InitCommand.cs`, the six installed `IProjectInitializer` implementations and their existing tests. The frozen Phase 4 proposal and Fabio's draft are design evidence, not competing current contracts; their original wording is preserved.

The older OpenSpec specifications below are **historical proposals**, not binding initial implementation instructions. Their adjacent `proposal.md`, `design.md`, `tasks.md` and supporting planning artifacts likewise preserve earlier intent; the Phase A cleanup put document-specific notices on those entry points. Historical task checkboxes are not an implementation backlog or proof of shipped behavior. Follow the [README's historical navigation](README.md#historical-navigation) instead of duplicating that inventory here.

| Retained specification | RETAINED for initial implementation | SUPERSEDED / DEFERRED / OWNER DECISION REQUIRED |
|---|---|---|
| [func new execution](../func-new-execution/specs/func-new-execution/spec.md) | Item-only scope, explicit `--template`, candidate-specific validation, noninteractive failure, force never bypasses eligibility. | **SUPERSEDED:** positional template, `--path`, upward search, listing gated on bundle. **DEFERRED:** public dry-run, lifecycle names, fallback `--param` aliases. **OWNER DECISION:** variant precedence, hidden IDs and later path placement. Current `--format` remains; old rendering flag is not restored. |
| [func init execution](../func-init-execution/specs/func-init-execution/spec.md) | `[path]`, installed Stack/language selection, adoption/healing, validation before force, required config failure. | **SUPERSEDED:** only empty/force project generation (occupied non-project targets preserve safe files). **DEFERRED:** public `--template`, `--non-interactive`, `--dry-run`, template-first UX, required action framework and removal of working initializers. **OWNER DECISION:** rollout and any stricter force policy. |
| [Template Engine integration](../template-engine-integration/specs/template-execution/spec.md) | Existing session/host context, catalog, group identity, func-host aliases, fail-closed eligibility and exact session-bound invocation are *required behavior*. | **SUPERSEDED:** projection as authoritative conditional/default evaluator and any widening of a full ID into siblings. **DEFERRED:** general immutable selection-criteria model, mandatory group collection interface, authored post-action plumbing and duplicate render-force policy. The required behavior is not yet implemented. |
| [Template constraints](../template-engine-constraints/specs/template-constraints/spec.md) | Raw declaration validation and fail-closed evaluation at listing/resolution; selected-content recheck before invocation. | **DEFERRED:** install/update preflight and Azure-Samples producer rules until those paths are owned. **OWNER DECISION:** exact installer/content integrity policy. Existing reader/evaluator helpers are not a registered command path. |
| [Template package install](../template-package-install/specs/template-package-install/spec.md) | Package provenance and failure-safe replacement remain future safety evidence. | **DEFERRED:** `func new install/update/uninstall`, remote acquisition, new package store and feed UX. Production registration of workload-delivered local content is a distinct, unresolved installer step, not an item-command side effect. |
| [Init quickstarts](../func-init-quickstarts/specs/func-init-quickstarts/spec.md) | Ordinary project-template extensibility is a future design input; current adoption/healing remains. | **DEFERRED:** template-first and multi-project UX, quickstart topology/actions and browse guidance. No positive coverage is inferred from this spec. |
| [Templating-system umbrella](../templating-system/specs/templating-system/spec.md) and [Azure-Samples pipeline](../azure-samples-template-pipeline/specs/azure-samples-template-pipeline/spec.md) | Historical program/producer inventory, separate from Template Engine ownership. | **SUPERSEDED:** automatic claim that all child specs jointly govern the first implementation. **DEFERRED:** ten-change completion gate and producer automation. Sarah/Francisco must approve release ownership and scope. |

No unresolved older `SHALL` clause is silently promoted: where the disposition is **OWNER DECISION REQUIRED**, public behavior stays as it is until that decision is made. The status notices do not erase or change the older proposals.

**Vocabulary:** Templates Architecture is the architecture name. Template Engine is the existing Microsoft.TemplateEngine-based templating engine. Templater is the existing Core Tools integration component. Runtime means the Azure Functions application/Functions Host runtime; Stack means an Azure Functions language stack. Earlier historical labels do not establish another current architecture concept. Existing source identifiers and public options, including PowerShell's `--runtime-version`, remain unchanged.

## Minimum model

The architecture requires **responsibilities and invariants**, not a separate named type or interface for each row. Implement each through an existing type, method parameter or small internal value where sufficient. The names below identify Fabio's proposed concepts, not a mandatory scaffolding inventory.

A family groups variants by `GroupIdentity`; an ungrouped template forms a family of one.

| Responsibility (possible shape) | Initial implementation requirement | Smallest justified representation |
|---|---|---|
| Factory, context and command-scoped engine owner | **REUSE EXISTING** | `ITemplaterFactory`, `TemplateEngineContext`, `TemplateEngineSession` and `Templater`: sync `Create`/`Dispose`, one session from discovery through invocation. |
| Installed descriptor, package information, kind and parameter presentation | **REUSE EXISTING** | `TemplateCatalogEntry`, `TemplatePackageInfo`, `TemplateType` and `TemplateParameterDefinition`; freeze issued collections, do not copy schemas onto candidates. |
| Family/candidate resolution | **REQUIRED RESPONSIBILITY** | Return eligible candidates in one family and exclusion reasons. `TemplateGroup` is optional; no mandatory collection interface, criteria language, service or second catalog. |
| Exact selected candidate | **REQUIRED INVARIANT** | Bind the issued descriptor, original engine candidate and selection-time verified content to a restricted session-bound handle. `ResolvedTemplate` is optional. Never re-resolve by name or invoke after disposal. |
| Canonical invocation inputs | **REQUIRED INPUTS** | Pass engine-evaluated canonical inputs and the selected handle. Use method parameters or an optional `TemplateInvocationRequest`; no duplicate evaluation state or caller-selected private workspace. |
| Private-render result | **REQUIRED OUTPUTS** | Return bounded planned file bytes, explicit directories and diagnostics through an existing internal value or optional `TemplateInvocationResult`. CLI publication effects are separate; the existing session owns temporary output. |
| Resolution diagnostics and publication | **SIMPLIFY / MERGE** | Reuse engine/reader diagnostics; add only distinctions commands consume. One concrete CLI-internal publication helper is justified only by shared guarded behavior, never a new public interface or transaction lifecycle. |
| Parallel `TemplateKind`, project-facts, global cache, general selection criteria, second evaluator, provider hierarchy, package manager or extracted assembly | **REMOVE / DEFER** | No initial implementation requirement warrants them. |

Only the selected candidate's session-bound handle can be invoked; a catalog descriptor cannot. Engine objects and evaluation types remain private to the integration. Exact signatures, including whether a particular value needs a named type, remain **OWNER DECISION REQUIRED**.

## Parameter evaluation

`TemplateParameterProjector` supplies names/aliases, types, choices and an unconditional `IsRequired`/string default for CLI presentation. These projected fields do not evaluate conditional requirements or defaults. Its tests explicitly show conditionally disabled parameters are not marked hidden and conditionally required parameters are not marked required (`test\Func.Tests\Templates\Engine\TemplateParameterProjectorTests.cs:88-100,141-155`). Neither field can decide final eligibility or prompting.

1. **CLI input:** retain ordered raw option occurrences with the original alias/token index, a *value-present* bit, the exact string (including `""`), and origin: explicit CLI, CLI-owned option, project-derived, prompt, or engine default. `--x`, `--x=`, `--x ""`, omitted `--x`, repeated `--x`, and an explicit value equal to its default must remain distinguishable. Reject conflicting explicit assignments to the same canonical symbol; never resolve them by last-one-wins.
2. **Before final variant selection:** Stage B checks the same raw tokens syntactically against **each eligible candidate's** projected aliases/types. Retain the compatible candidates and their individual rejection reasons; do not make requiredness, conditional or default decisions from projected fields. Missing values that may be prompted for remain candidates.
3. **After exact selection:** bind CLI/Stack values to canonical names, including the CLI-owned `name`; supply these with the selected original `ITemplateInfo` and captured host context to **Microsoft.TemplateEngine's** parameter evaluation. The engine is authoritative for authored/evaluated defaults, enabled/disabled states and conditional requiredness. Reuse its input/evaluation path (historical pinned-version `InputDataSet` and `TemplateCreator` calls are static prior art, not proof of present wiring); do not implement a second expression evaluator.
4. **Before invoking:** use the evaluated result to reject invalid explicit/disabled inputs, prompt only unresolved *active* required values when interactive, and re-evaluate after prompts using the same session/context. Without input, report missing canonical names/aliases; do not replace explicit empty/invalid input with defaults. Pass the engine-evaluated input set, not a reconstruction from presentation fields, into the exact template invocation.
5. **Unsupported feature:** if the retained engine or bounded adapter cannot evaluate a required conditional/default expression or report its failure for the first template, classify that candidate as unsupported and refuse creation **before target effects**. Obtain a representative fixture proving conditional required, conditional disabled, dependent default, explicit-empty, and explicit-default-equal semantics before enabling a public path.

The command owns *provenance and prompting*, not authoring-expression semantics. Exact evaluation API shape is an internal implementation proof gate, not permission to guess a default.

## Selected content and constraints

Selecting a template by its identifier does not guarantee that its content remains unchanged. Before rendering, the CLI must verify that the selected content matches the content captured at selection.

On listing/resolution, read the raw mounted `.template.config\template.json` for each considered candidate through the same engine mount, decode its contents and pass them with available package-type metadata to `TemplateConstraintDeclarationReader.Read`. Classify unreadable/malformed declarations as unavailable with diagnostics before engine normalization can discard them. Use existing `FuncHostTemplateMetadataReader` for func-host aliases. Do not derive a path from an unchecked user reference.

Evaluate recognized engine constraints through its existing manager and func workload/bundle requirements through `FuncTemplateConstraintEvaluator` with one captured `TemplateConstraintEvaluationSnapshot`; currently `FuncTemplateComponents.AllComponents` is empty and no command wires this evaluator. `Failed`, `NotEvaluated` and `Restricted` block selection/invocation even with force; keep every rejection reason. Unknown *well-formed* types remain valid declarations but are not evidence of eligibility. Invalid installed content must not turn into an empty-success catalog.

**Selection-time binding:** after candidate-local syntax filtering and final candidate choice, but **before** issuing a handle that can be invoked or prompting, capture that candidate's content within defined asset limits. Record package/mount identity and a deterministic manifest of relative asset paths and bytes/digests, including raw template configuration and func-host metadata. Reject absent, unreadable, unsafe or over-limit assets at binding.

For mutable folder content, make a private bounded copy **at this point**, verify every copied asset against the selection-time manifest, and bind rendering to that verified copy. For versioned installed content, direct rendering from its mount is permissible only when the installer provides an enforceable immutable, integrity-checked version for the session; otherwise use the same verified-copy path. Do not make a late copy without comparing it to the selection-time baseline.

Before private rendering, recheck **that handle's** source package/mount, raw configuration and asset manifest against the bound baseline. A changed, missing or unreadable asset/configuration invalidates the handle with "rerun selection" guidance; no sibling substitution or target effects. Rendering must consume the bound private copy or enforceably immutable mount, **not** a mutable original beside an unused copy.

If the pinned engine cannot instantiate the exact selected candidate against that verified content, fail the initial implementation capability gate rather than invent a new engine environment or claim safety. The exact mount/rebinding API remains an internal proof gate; the captured content is specific to the selected candidate, not a global filesystem snapshot, and concurrent filesystem atomicity is not promised. Installed package replacement coordination remains an owner decision.

Acceptance: corrupted raw declarations omitted by the engine cache stay blocked; duplicate constraints, changed `template.json`, changed host config, unsupported constraint, and assets changed **between selection and copy** or **between selection and invocation** fail without sibling invocation or target writes. Missing/unreadable assets fail at binding or recheck. Unchanged selected content renders from the bound view through the same handle.

## Token capture

The CLI preserves template-specific arguments until it can validate them against eligible template candidates. Stage A captures those arguments before root parsing; Stage B parses them separately for each eligible candidate before final selection.

`NewCommand` currently has one positional `[path]`, built-in flags and `--format`; `PathArgument` may receive an unknown option's value as the path. Do not add a positional template selector or mutate the shared `System.CommandLine` (SCL) graph.

1. **Stage A at the CLI entry for `new` only, before root parsing:** recognize exact known built-ins and their arity, help/global options and *one* optional positional path **before any template-specific option**. Record all remaining original argv tokens in order with indices, repeated spellings and exact inline/separate/empty-value syntax. Do not infer the arity of an unknown option and do not pass it **or its possible value** to `PathArgument`. Reject removed public flags (`--path`, `--output`, `--dry-run`) and the known `-name` typo as stable grammar errors; leave other candidate-specific aliases for Stage B. `--` and help follow the existing CLI rules.
2. **Stage A result:** pass only recognized built-ins and the unambiguous `[path]` to the existing parser. Carry the untouched staged sequence as an immutable *per-invocation* value to `NewCommand` (no global/static token store or singleton command mutation). `WorkingDirectory.FromExplicit` resolves a supplied path once; otherwise use cwd. A bare token after a template-specific option is **never silently reclassified as a path**.
3. **Resolve a family or pin a full ID:** after project context and installed-content eligibility, identify the requested family, or retain **only** the exact full-ID/catalog-entry candidate. No final variant selection or prompt yet.
4. **Stage B before variant selection:** run the **same opaque token sequence independently against every eligible candidate** in that family (or the sole pinned candidate), using its projected aliases/types. Preserve token order, duplicates, explicit empty values, missing-value and unconsumed-token diagnostics **per candidate**. Narrow only by syntactic compatibility; an unresolved conditionally required value does not eliminate a candidate. If no candidate parses, report the relevant per-candidate errors; an exact full-ID failure never chooses a sibling.
5. **Final selection, then engine evaluation:** command policy/prompt chooses from syntactically compatible candidates; only then bind its content at selection and issue the exact session-bound handle, bind canonical occurrences and let the retained engine evaluate defaults and conditional semantics. Invalid explicit input fails; prompt only for unresolved active required values and re-evaluate in the same session. Do not run semantic evaluation on one candidate and quietly change selection afterward.
6. **Path/error boundary:** an explicit path after a template-specific option is ambiguous without its schema: for the initial implementation **reject it and instruct the user to put `[path]` before template-specific options**, rather than running in cwd. If no explicit path was captured and cwd is not a project, report that failure with the same move-path hint when opaque bare tokens follow template options; do not reinterpret a possible parameter value as a path. Broader option-order UX requires an owner decision. Stable-parse or Stage B failure causes no target mutation.

Focused cases: `new . --template X --flag value`, `new --template X . --flag value`, `new --template X --flag=value` (cwd), two variants with different option contracts, repeated options, `--flag=`, `--flag ""`, unknown-option-before-path, `--flag value .` (diagnostic), `-name` typo, invalid removed flag, `--` behavior and no unrelated command parsing changes. If a SCL extension cannot pass the staged tokens per invocation, implement the narrow command-entry handoff rather than accepting path misbinding.

## Non-force init

Project initialization preserves safe existing scaffold files without force; item creation refuses create collisions. `InitCommand` currently creates its target early and calls workload initializers; `ProjectFiles.WriteIfMissing` preserves an existing scaffold file in a non-force run (`src\Abstractions\Projects\ProjectFiles.cs:24-39`). A nonempty directory *without* Functions project markers is not automatically an existing Functions project.

| Destination state | Proposed `func init` treatment | Safety / outcome |
|---|---|---|
| Fresh empty or occupied **non-project** directory, no force | Select Stack and qualified project content. Keep safe existing scaffold files byte-for-byte; write missing files/directories, and record **skipped/preserved** paths. | Validate the combined preserved-plus-generated project before claiming success. If a preserved file prevents valid output or required config, fail with guidance rather than overwrite or report success. No blanket "empty or force only" rule. |
| `host.json` project without CLI config, no force | Preserve current adoption; create/merge only required CLI config. | No Templater or project scaffolding. Keep unrelated keys and single-language omission. Required config-write failure is nonzero. |
| Healable CLI config, no force | Preserve language detection and config merge. | No Templater or project scaffolding; failure to persist is nonzero. |
| Complete initialized project, no force | Preserve current refusal. | No target effects. |
| Readable malformed config | Diagnose without force; with explicit force, allow regeneration after other validation. | Unreadable/unsafe files are not treated as replaceable malformed config. |
| `--force` | After Stack/parameter evaluation, private rendering and all preflight, confirm when input is available; preserve the existing no-input authorization policy. | Recheck the exact deletion set and ancestors, preserve `.git` directories **and pointer files**, then report deletions plus resulting writes. Decline/cancel before cleanup leaves target untouched. No rollback promise. |

`func new` **item create** without force instead refuses a create collision. Stack-owned append/modify is a distinct guarded effect and must not inherit init's write-if-missing rule. Existing Stack initializers stay in service until a replacement is qualified; this paragraph is the **migrated route's** contract, not a claim that the current early-mutation defect was fixed. A stricter public force/confirmation policy requires Francisco/team approval.

## Exact ID selection

An exact full-ID request selects only that variant. Alias and family matching must not replace an exact choice.

1. Within the selected source, check **full template identity first**, before project/Stack/language/argument filtering. One full-ID match pins *that exact variant*; wrong kind, invalid content or failed eligibility yields its diagnostic rather than falling through to an alias. A higher-precedence or eligible sibling cannot replace it. Duplicate applicable full IDs fail as an identity collision, not first-one-wins.
2. Only when no full ID matches may a declared short name/alias match (case-insensitive as supported). Partition matches by explicit `GroupIdentity`, with ungrouped templates as identity-keyed singletons. Multiple families remain ambiguous; an alias matching just one variant **does not widen** to unrelated siblings. A deliberate family selection may expose variants sharing that family/alias.
3. For an issued catalog entry, retain its same-session exact **candidate identity**; if the picker selected a **family**, resolve that family explicitly. Apply candidate-local syntactic filters before CLI-selected precedence or prompt. Bind verified content and issue the handle that can be invoked **only after** the final candidate is chosen. Do not silently drop a full-ID choice because another variant accepts its parameters.
4. With zero eligible variants keep specific exclusion diagnostics; with several remaining, the CLI prompts or reports ambiguity noninteractively. The group is an immutable view, not a policy service. If historical case-insensitive full-ID lookup is required, apply it only after exact spelling, refuse case-collisions and never fall through to a sibling alias after an ineligible ID. Confirm alias/precedence policy with Fabio.

Acceptance: full-ID ineligible plus eligible sibling, full-ID with incompatible tokens, alias matching only one sibling, two groups sharing an alias, duplicate full IDs, explicit catalog selection, zero candidates and post-disposal invocation. No test may pass by re-resolving a name after the user's choice.

## func init

**Proposed migrated route; existing six Stack initializers remain operative until their replacement is qualified.**

| Stage / owner | Input -> output and pre-existing seam | Failure boundary |
|---|---|---|
| Entry / `Program`, `InitCommand` | After first-run setup, parse `[path]`, Stack, language, name, force and contributed options. | No target directory creation during parsing; first-run may return setup success with rerun guidance. |
| State / CLI | Inspect markers, CLI config and target safety -> fresh/occupied non-project, adopt, heal, complete, malformed or unsafe. | Adoption/healing bypass engine; complete project refuses without force; unsafe inputs are not force-recoverable. |
| Select/validate / CLI + Stack | Installed initializer selects Stack/language; validate all options, including .NET framework, against actual target and package availability for the initial implementation. | Reject bad input before cleanup. Default project-template choice for the selected Stack/language; no new public `--template`. |
| Resolve/evaluate / `Templater` | Create one command session; list project entries, validate raw constraints and candidate-local input syntax, choose the qualified default, then bind its content/handle and engine-evaluate inputs. | No matching/valid content -> actionable failure; no fallback to another project template after a selected failure. Unmigrated Stack still uses its existing initializer. |
| Render/compose / engine + Stack | Render into private bounded output; combine generated files/empty directories with Stack settings and required CLI config bytes. | For an occupied non-project target, include preserved files in combined validation. An unsupported action or invalid final project fails before target effects. |
| Preflight/publish / CLI | Validate paths, originals, skips, cleanup/force authorization, then publish permitted changes and config last. | Distinguish skipped files from a collision; protect Git metadata. Config failure is nonzero with completed file/deletion effects. |
| Finish / CLI | Run only known Stack setup after prerequisites, report files actually created, modified, preserved or deleted and final status, dispose session on every path. | Cancellation/cleanup/setup failure is reported separately; no whole-operation rollback. |

## func new

**Current command still returns the migration diagnostic; the following is not implemented.**

| Stage / owner | Input -> output and pre-existing seam | Failure boundary |
|---|---|---|
| Entry/token capture / CLI | After first-run gate, staged `[path]` and built-ins plus untouched template tokens. | Unknown values never bind as path; parse failures do not initialize an engine or change the target. Preserve `--format`. |
| Project/Stack / CLI | Use `IFunctionsProjectResolver` for the *explicit target*, capture Stack/language and resolved bundle/workload inputs. | An unrecognized project or unsupported item Stack fails with guidance; no upward search is introduced. |
| Discover / `Templater` | One session lists installed item entries in the chosen source/channel scope. | Ordinary list excludes hidden items, retains unavailable diagnostics, and does not impose creation-only bundle gates. Missing/corrupt registration is explicit, not empty success. |
| Resolve family / Templater | Apply full-ID pin or scoped alias/family semantics, then Stack/constraint eligibility to obtain a candidate set. | Wrong kind, failed/unknown constraint, ambiguous family and exact-ID ineligibility remain distinguishable; no sibling substitution. |
| Stage B / CLI | Parse the same ordered template tokens **separately for every eligible candidate** using its projected aliases/types; retain syntax-compatible candidates and each rejection reason. | No candidate accepts the input -> targeted errors; a full-ID request has only its pinned candidate, even if a sibling's parser would accept the tokens. |
| Select/bind / CLI + Templater | Apply approved command policy or prompt to the syntax-compatible set; establish a selection-time content baseline and issue one exact session-bound handle. | Multiple remaining variants need an owner-approved policy or prompt; changed/missing/unreadable assets prevent issuing a handle that can be invoked. |
| Evaluate / CLI + Stack + engine | Bind CLI/project values to the exact candidate's canonical input; engine evaluates conditional required/enabled/default semantics. | Invalid explicit values fail; interactive prompt only for unresolved active required inputs; noninteractive fails with syntax. |
| Invoke/compose / exact handle + Stack | Recheck the bound source view; instantiate privately from the verified copy or enforceably immutable mount; prepare bounded files/explicit directories and any Stack-owned edits. | Unsupported engine feature or content changed since selection fails without target mutation; do not execute arbitrary package actions. |
| Publish/finish / CLI | Validate collision, originals, ancestry and force, publish guarded effects, report actual status and dispose session. | Item create collision fails without force; partial/canceled/required-follow-up failure is not success. No init config write on this command. |

## End-to-end proof

Choose a representative **real** project package and one parameterized item package for the same supported Stack, both installed in an isolated CLI hive. `func init` must create the expected project scaffold, preserve approved existing files where applicable and persist required `.func\config.json` last (omit language for a single-language Stack as the current command does).

On a later `func new [path]` invocation, the normal project resolver must identify that actual Stack/language and the same installed item content; full-ID selection must invoke the requested variant, and an explicitly supplied required value must appear in the generated function. Verify actual function discovery with the approved Functions Host Runtime qualification. Existence of `host.json` or CLI config alone is not proof.

The currently checked-in basic text-file template fixture cannot satisfy this proof. Add a representative approved fixture and a multi-variant selection case; neither is an authenticated production package. Two templates establish **ENGINE / COMMAND PROOF**, not all-Stack or GA acceptance.

## Workload registration

The engine hive is the Template Engine's installed-template storage and registration location. Existing paths have distinct responsibilities:

- Test-only `TemplateFixtures.InstallAsync` explicitly registers a folder in an isolated hive.
- `TemplaterFactory` discovers the Template Engine's shared CLI-home `templates` hive.
- `WorkloadDeploymentService` installs validated workload payloads and store metadata.
- `.NET` init currently uses a separate `dotnet new` hive.

None provides the missing production registration of workload-delivered template content into the CLI Template Engine hive.

**Minimal proposed production seam:** installation/setup (owner to confirm with Sarah/Fabio) receives a validated local template package path under an installed, versioned workload; after payload validation and **before reporting template content ready**, it uses the existing Microsoft.TemplateEngine managed provider to register that local package into the existing CLI hive. Do not scan arbitrary directories or install on discovery. Sarah must identify the actual package-relative path and version/channel/format contract; do not invent `tools\` layout for the template package.

On missing content, unsupported metadata or failed registration, report incomplete template availability, retain any prior usable registration when possible, and expose an explicit repair/setup path. Registration/retry must be idempotent against the installed workload identity/version and must not silently report success after a partial failure; future replacement/uninstall coordination and stable-vs-preview precedence require owner decisions.

The initial implementation may use explicit test-only local registration to prove command integration, but **public availability waits for approved production registration**. No second registry, package manager or new `func new install` command.

## Rendering and publication

The exact selected candidate passes **engine-evaluated** canonical inputs to the pinned engine creator. Rendering must use the **content bound at selection**, never a newly copied or re-resolved source. The binding and recheck requirements are defined in [Selected content and constraints](#selected-content-and-constraints).

Render into bounded private output owned by the existing session; do not allow authored post-actions, target writes, unapproved custom bind sources or unbounded asset expansion in the initial implementation. Preserve the existing func host bindings needed by the chosen template.

Capture final relative paths and bytes plus explicit empty directories; reject an unsupported effect/feature *before* touching the target. Return a typed render outcome with **planned** effects and diagnostics, never a success-shaped empty fallback. No extra output lease or prepared-operation service unless a real resource requires one.

CLI-owned preflight validates paths, ancestor links, case/path aliases, original bytes, create/replace/modify intent, configuration and exact `--force` deletion set. Init without force preserves safe existing scaffold files and records skips; item create without force refuses collisions. Guarded replace/append needs an unchanged original even with force. Recheck permissions/originals immediately before each effect using the existing filesystem seam, and qualify OS-specific no-replace/replace behavior before activation. `IFileSystem.MoveFile(overwrite: false)` alone is not guarded replace; `DirectoryGuard.ClearExceptGit` deletes `.git` pointer files and is not safe unchanged.

Persist required CLI configuration **after** successful project file effects; apply required Stack follow-up only after its prerequisites. A failure to write fresh, adopted or healed configuration is nonzero. Report files actually created, modified, preserved or deleted, and identify pending or uncertain effects.

Propagate cancellation before irreversible work and between steps; do not erase completed effects or promise rollback. Cleanup errors are distinguishable from generation success; `TempDirectory.Dispose` currently swallows errors and cannot alone prove cleanup. Unexpected internal errors are not converted to user-input errors.

## Historical behavior and F1-F6

These are **proposed** dispositions grounded in [the Phase 4 matrix](CLI-v5-Templates-Fabio-First-Object-Model-Proposal.md#10-historical-behavior-dispositions) and existing source; historical research links and some content bytes are missing. No historical test assertion is an executed pass.

| Behavior / finding | Classification and disposition |
|---|---|
| F1: competing old architecture | **PRESERVED BY DESIGN:** Fabio's starting model; former extraction/action architecture is historical. The Templates Architecture README and Phase A notices prevent old specs, designs and task lists from governing the initial implementation. |
| F2: grammar | **CURRENT SUPPORTED:** `[path]`, `--format` in `func new`; former positional template, `--path`, `--output` and public dry-run are **DEFERRED / SUPERSEDED**. |
| F3: force before validation | **CURRENT DEFECT; IMPLEMENTATION REQUIRED:** validate Stack/framework, template values and combined output before cleanup; protect `.git` files and directories. |
| F4: config false success | **CURRENT DEFECT; IMPLEMENTATION REQUIRED:** required finalization failure is nonzero for fresh init, adoption and healing, with actual effects. |
| F5: first-run preemption | **CURRENT SUPPORTED / QUALIFICATION:** successful setup can short-circuit either command; rerun with fresh workload view. Do not claim unconditional migration exit 1. |
| F6: missing research/payloads | **EVIDENCE GAP:** use pinned summaries, do not infer artifact-exact parity; request approved content before release claims. |
| Template IDs/aliases/parameters/defaults | **HISTORICAL EVIDENCE; PRESERVED BY DESIGN** for supported IDs and known aliases. Engine evaluation, strict explicit input and actual package coverage are **IMPLEMENTATION REQUIRED**; new alias rules need an owner decision. |
| Compatibility/listing/selection | **PRESERVED BY DESIGN:** recognized required constraints fail closed; list may describe unavailable content. Profile/no-bundle policy, precedence and hidden-ID behavior are **OWNER DECISION REQUIRED**. |
| Six project Stack initializers; adoption/healing | **CURRENT SUPPORTED:** keep operative, including non-force write-if-missing, until qualified project-template replacement. |
| Node/Python/.NET item creation | **HISTORICAL EVIDENCE; CURRENT IMPLEMENTATION GAP:** do not call available until real content and command path pass qualification. Go/Java/PowerShell historical negative item cases need GA scope decision. |
| Force, collisions, cancellation and partial effects | **PRESERVED BY DESIGN** for authorization/observable outcomes; unsafe ordering, `.git` pointer deletion and false-success paths need fixes. Platform-safe guarded publication remains **IMPLEMENTATION REQUIRED / RUNTIME VALIDATION REQUIRED**. |
| Quickstart, install/update/search, multi-project actions | **DEFERRED:** retain current Quickstart behavior separately; broad content/lifecycle proposals are not initial Template Engine requirements. |

## Team review and owner decisions

The Phase 7 review package starts at the [Templates Architecture README](README.md), then this current proposed contract, the frozen Phase 4 predecessor, Fabio's source draft, the independent Phase 5 and Phase 6 reports and the linked historical specifications/planning notices. The README inventories historical entry points; do not reapply their old checkboxes. Ask Fabio/Sarah/Francisco to decide:

1. **Fabio + CLI owner:** initial Template Engine parameter-feature subset, exact internal signatures, SCL token-handoff details and alias/variant precedence. Full-ID pinning and fail-closed constraints are correctness requirements, not policy options.
2. **Sarah + Fabio:** approved real item/project fixtures, workload package-relative location, registration owner/timing/retry, and version/content integrity. No positive public `func new` path before real content is available.
3. **Francisco + team:** supported-Stack/GA inventory, any stronger `--force` confirmation or occupied-target behavior change, hidden-ID/profile/no-bundle policy, and public command activation. Do not silently replace working initialization.

The initial implementation requirements are coherent, but these consequential choices are **OWNER DECISION REQUIRED** before public activation. Independent architecture review, team approval and Functions Host Runtime qualification remain distinct gates.

## Implementation sequencing

| SAFE TO START BEFORE TEAM REVIEW (future authorized implementation only) | WAIT FOR FABIO/SARAH / team approval |
|---|---|
| Characterize pinned engine parameter evaluation and private rendering in isolated **tests**; add parameterized function and project fixtures without modifying supported commands. | Freeze invocation signatures/feature set, exposed options or precedence across variants. |
| Extend internal tests of the existing raw constraint reader and session disposal; specify future changed-configuration/foreign-handle cases without freezing unapproved signatures or activating a public command. | Production workload-to-hive registration owner, layout, version/retry policy and content package migration. |
| Write parser-boundary tests for `[path]` and unknown option/value ordering using the current command grammar; keep tests reversible and internal. | Route public `func new` to generation, replace a working `func init` initializer or change `--force` behavior. |
| Assess and test existing non-force `ProjectFiles.WriteIfMissing` and adoption/healing outcomes; no new public API or filesystem policy. | Declare two-template proof complete GA coverage, support a new Stack's item templates, or ship release content. |

**Initial implementation order:** after explicit authorization, start with these internal tests and a representative local test package. Next, with Fabio/CLI contract approval, add small session-bound resolution/engine evaluation/private render internals; with Sarah's content and registration decisions, wire one real `func new` route and then one migrated `func init` project route. Run public command and OS qualification before activation. No lane is allocated and no product work is performed by this document.

## Provenance and review history

**Baseline:** `feature/templates-engine-redesign`, HEAD `c8559f8e1f0ba0bd3e1fed39e68934c836f5b32b`, tree `3243a0529b2505f39e76ba2b0543545ff77b905c`.

**Frozen predecessor:** [Fabio-first Phase 4 proposal](CLI-v5-Templates-Fabio-First-Object-Model-Proposal.md), SHA-256 `3040ebc1657475f6ecbb6b22e495995ad562c283e61965e465731bb99cb182be`. This file supplements, but does not edit, that proposal.

**Phase 5 review:** Independent Principal report (current session display `Principal Review - Phase 5 - 10-31`, previously labeled 10-30), report-text SHA-256 `d258e45cde9c0c49dabd0f1a7fab50c135fb19243ed13389f7bf0445e9e88cd7`.

**Phase 6 review:** Independent Principal delta report, report-text SHA-256 `96b59e21a6f952bb01f2ab44e1c682f48d18b8e842f4a44c010777cb4ce5e0bd`. Phase 6 passed independent Principal review, including the architectural simplicity gate.

Both reports were read from their local read-only session transcripts during the preceding design transaction. The [Phase A documentation entry point](README.md) also received an independent Principal **PASS**, not implementation approval; its historical notices and navigation remain preserved during editorial reconciliation.

The [Fabio draft](CLI-v5-Templates-Object-Model-Fabio-Draft-2026-10-30.md) is the starting direction; the [execution plan](CLI-v5-Templates-Engine-Redesign-Execution-Plan-2026-10-30.md) records phase gates, subject to this document's current requirements and separate authorization. [Architecture Reset](CLI-v5-Templates-Architecture-Reset-Clean.md) and [Working Design](CLI-v5-Templates-Working-Design.md) remain historical evidence. Their missing linked research reports and unavailable historical package bytes limit artifact-exact parity claims.

Phase 5 identified `TemplateCreator.InstantiateAsync` and `InputDataSet` as historical same-version prior art, not current command wiring. Review finding IDs are retained here for traceability, not as architecture terminology:

| Review finding | Current design section |
|---|---|
| P5-01 | [Parameter evaluation](#parameter-evaluation) |
| P5-02 / P6-01 | [Selected content and constraints](#selected-content-and-constraints) |
| P5-03 / P6-02 | [Token capture](#token-capture) |
| P5-04 | [Non-force init](#non-force-init) |
| P5-05 | [Exact ID selection](#exact-id-selection) |
| P5-06 / P6-03 | [Document status and requirements](#document-status-and-requirements) and the [Templates Architecture README](README.md) |
| P6-04 | [Vocabulary](#document-status-and-requirements) |
| P6-05 | [Minimum model](#minimum-model) |

## Tests and qualification

| Requirement / gate | Focused future acceptance cases (not executed here) |
|---|---|
| Parameter evaluation | Conditional required/disabled/default expression; omitted versus explicit-empty, repeated option, explicit-default-equal; prompt only unresolved active input, engine-evaluated value appears in generated function. |
| Selected content and constraints | Raw duplicate/invalid/unknown constraints; selection-time manifest/copy, asset changed **before copy** or **before invocation**, missing/unreadable asset or configuration; render from bound view only, no sibling or target writes. |
| Token capture | `new . --template X --param v`, built-ins before path, `--param=` and `--param ""`, unknown before path, after-option bare path, typo, `--output`, `--`, and non-`new` parser regression. Use two variants whose accepted aliases differ: Stage B must remove the incompatible one **before** selection; a pinned full ID fails rather than selecting its sibling. |
| Non-force init | Occupied non-project directory preserves scaffold files and reports skips; invalid combined project refuses success; adoption/healing skip Templater; force accepts/declines/cancels safely, including `.git` pointer. |
| Exact ID selection | Full-ID ineligible with eligible sibling, alias on one variant, two groups on one alias, duplicate IDs, selected catalog entry, precedence tie, foreign/stale/disposed handle. |
| Document status | README, seven spec notices and Phase A design/task notices link to the current proposed authority; historical checkboxes/`SHALL` clauses do not become implementation instructions. |
| Vocabulary and minimum model | Runtime/Stack/Template Engine vocabulary check; responsibilities can use existing internals or small values without mandatory new interfaces, request/result wrappers or lifecycle owners. |
| Complete journey | Real parameterized project/item package, `init -> new -> Host discovery`, required config failure and partial effects, missing content, cancellation, installed OS/RID distribution and platform-safe publication. |

Existing tests and CI were inspected statically in the preceding design transaction. This editorial reconciliation runs documentation checks only, not restore, build, tests, package installation, executable probes or Runtime investigation. **RUNTIME VALIDATION REQUIRED.** The first internal tests are not a release qualification substitute.

**Next documentation gate:** provide this editorially reconciled file and its SHA-256 to Francisco for engineering-editorial/EER review when separately authorized. After editorial acceptance, obtain separate authorization for a documentation checkpoint that preserves unrelated changes. Any later rename to `docs\templates\architecture.md` is a separate transaction; no checkpoint, rename or implementation is authorized by this document.
