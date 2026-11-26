# Templates Architecture

**Status:** Proposed

## Overview

The proposed Template Engine is intended to let `func init` create a Functions project and `func new` add a function using installed templates. When complete, it will discover templates, use their metadata to find suitable choices, evaluate template parameters, and invoke the selected template.

The initial goal is a working local journey: `func init` creates a valid project; `func new` adds a parameterized function; the Functions Host discovers that function. A small set of representative templates can establish this integration before the complete template catalog is migrated. This is not, by itself, release qualification.

The first slice is .NET 10 isolated and C#, with the project, HTTP trigger, and Timer trigger templates supplied by the .NET workload. Other Stacks, custom constraints, standalone package management, and remote search follow separately. Language and template kind select the initial templates using the default programming model.

## Current foundation

The current code provides a command-scoped `Templater` using the persistent CLI template hive under `<CLI home>/templates`. `Templater.ListAsync` returns installed templates in a `TemplateResolutionResult`; `ListProjectTemplatesAsync` uses native type and language filters, and `FuncTemplateInfo.InstantiateAsync` delegates to `TemplateCreator`. Each `FuncTemplateInfo` forwards the engine metadata, keeps the original `ITemplateInfo`, and exposes parsed `func.host.json` metadata through `HostData`.

Host metadata uses the dotnet host JSON shape: `symbolInfo` supplies parameter aliases and presentation flags, with `usageExamples` and template-level `isHidden` alongside it. `HostData.SymbolInfo` exposes a read-only dictionary of `FuncTemplateSymbol` values keyed by symbol name. Loading prefers the engine's cached `ITemplateInfoHostJsonCache.HostData` content and falls back to the selected `HostConfigPlace` through the engine mount when cached content is unavailable. An absent configuration yields empty metadata; malformed metadata or an unreadable declared file fails explicitly.

`func init` uses this discovery API for template-driven project initialization. It filters project candidates by language, selects the unique `func-{stack}` short name, binds host aliases to native parameter definitions, dry-runs validation, and instantiates the result. With no candidates, it falls back to the existing Stack initializer; when candidates exist without a unique default, it fails rather than silently falling back. `func new` does not use this API yet. A workload can declare a `templateRoot` in `workload.json`. The workload store persists it and hydrates the package root, and a workload template package provider exposes active runtime workloads' roots without installing them in the CLI hive. The current foundation does not evaluate Functions-specific constraints, verify selected-content integrity, render privately, or provide the guarded publication described below. The rest of this document describes proposed work beyond the current `func init` integration.

<a id="end-to-end-proof"></a>

The first working journey defines the representative project, item and Functions Host proof.

The Template Engine is part of CLI v5. Moving it into a separate assembly is outside the initial scope.

## Proposed design

The proposed command-facing flow is:

1. The CLI parses the command and identifies the project or requested language stack.
2. The template factory creates a `Templater` with the relevant context and DI-registered engine components.
3. The `Templater` uses Microsoft.TemplateEngine to discover templates from the template roots that the active workloads declare; the engine manages its cache.
4. The CLI presents choices when necessary and supplies template-specific arguments.
5. The Template Engine resolves one exact template, evaluates its parameters, and renders it.
6. The CLI validates and publishes the resulting files and reports the outcome.

| Component | Responsibility |
| --- | --- |
| CLI commands | Parse public options, identify project and language stack, prompt for input, authorize filesystem changes, persist project configuration, and report results. |
| Template factory and context | Create the command-facing `Templater` with the installed-template scope, required command context, and DI-registered engine components. |
| `Templater` | Expose installed template metadata and the operations needed to list, resolve, and invoke templates. |
| Template catalog entry | Describe an available template, including identity, metadata, and parameters; it is not itself permission to invoke a template. |
| Template group | Present related template variants together when useful for selection. |
| Resolved template | Represent the exact selected template that can be invoked within its command scope. |
| Template Engine internals | Use Microsoft.TemplateEngine facilities for metadata, authored parameter semantics, and rendering. Keep engine resources within the command-scoped templater. |
| Content producers and workload installation | Build and package templates. A workload declares an optional `templateRoot`; installation persists it and extracts the package payload. The engine reads each active workload's root in place, so install, update and uninstall need no template registration step. |

The CLI flow captures command and project context for `func init`. The current `Templater` accepts a context and passes it and the DI-registered engine components to the func host. Template-driven project initialization uses package-manager filtering and `TemplateCreator` dry-run/instantiation; it does not introduce catalog, group, or resolved-template types.

`FuncTemplateInfo` keeps the original engine template available as `Template` and forwards its metadata, including `HostConfigPlace`. Its factory parses host configuration before construction, and the read-only `HostData` remains usable without an open mount. `TemplateResolutionResult` currently holds the discovered candidates; it does not group or select them. Catalogs, groups, and resolved templates remain design concepts for later work, not current types.

For later selection, the proposed design calls for immutable catalog and group views and one canonical representation of parameter metadata. Only an exact selection issued by the `Templater` should be invoked. Selections from another command scope, or selections that are stale or disposed, must fail.

Template invocation is implemented for template-driven project initialization through `TemplateCreator`. It does not yet render to a private staging area or provide the broader selected-content and filesystem publication guarantees proposed below.

These are responsibilities and useful object-model concepts, not a requirement to introduce a public class or interface for each row. Reuse existing types where they fit. The proposed group, resolution, request, and result shapes inform the command-facing API; their exact signatures still need agreement against concrete command calls.

**Terminology:** A *Stack* is an Azure Functions language stack, such as Node.js or Python. *Runtime* means the Azure Functions application/Functions Host runtime, not a language worker. A `hive` is the engine's isolated persistent settings, managed-package state, and cache location; it is not necessarily where a provider's template payload lives. Core Tools v4 refers to the legacy CLI; CLI v5 is the new implementation. There is one component named **Template Engine**; the previous unshipped implementation is not the design to reproduce.

## Installed sources, hive, and cache

`Templater.ListAsync` currently lists the templates found in the roots that active workloads declare, plus any packages installed in the isolated CLI hive. The API does not provide template registration or installation. `func init` uses the filtered project-template API; `func new` does not use it yet.

`TemplaterFactory` uses `<CLI home>/templates` for `EngineEnvironmentSettings`. Microsoft.TemplateEngine stores `templatecache.json` under its host-version settings directory inside that hive. Its managed provider also keeps installed-package state in the hive. Workload payloads remain in the workload store; exposing them through a read-only provider still uses the same engine and hive. There is no second CLI-owned template inventory or cache.

### Cache lifetime and invalidation

The engine manager and its in-memory source/cache state live for the command-scoped `Templater`. The on-disk cache survives process exit. On `GetTemplatesAsync`, the engine compares the supplied mount paths and timestamps with the cache:

- Added or removed mount paths rebuild the cache, including removal of stale entries for sources no longer supplied.
- A `LastChangeTime` strictly newer than the cached timestamp rebuilds it. Equal or older timestamps do not.
- Missing/unreadable cache data, an incompatible cache format, or a changed UI locale also cause a rebuild.
- Otherwise, the engine reuses the cache rather than scanning every template again.

The CLI loads the active workload inventory at startup. `WorkloadTemplateSource` supplies existing declared roots, with `LastChangeTime` equal to the later of the root-directory and `workload.json` write times. After workload install, update, or uninstall, the next CLI process supplies the new inventory; the engine refreshes lazily when discovery next runs. The workload installer does not eagerly rebuild the template cache.

The workload provider does not watch for mutations during an existing command. Its source list is cached by the engine manager. Same-version reinstalls require an advanced timestamp; arbitrary nested-file edits without that signal are not detected. These timestamps are not content hashes or locks. Missing roots are omitted, so a new command drops their cached entries, although the missing-root diagnostic is still pending.

`TemplaterTests` exercises the real engine with isolated persistent hives and the workload provider: source addition/removal, a deleted root, active-version replacement with the old payload retained, unchanged-timestamp reuse, and same-version refresh through a newer manifest timestamp. This qualifies discovery across fresh engine instances, not concurrent filesystem mutation during rendering.

### Delegation to Microsoft.TemplateEngine

| Responsibility | Owner |
| --- | --- |
| Workload acquisition, extraction, ownership, active-version selection, and declared roots | CLI workload infrastructure |
| Mount folders/archives, find template definitions, parse native template metadata, build/reuse the local cache, and normalize duplicate identities | Microsoft.TemplateEngine |
| Native filtering, parameter evaluation, and instantiation APIs | Microsoft.TemplateEngine; `func init` uses them, and `func new` integration is pending |
| Project/Stack context, supported command behavior, prompts, errors, destination permissions, and explicitly supported follow-up work | CLI commands and services |
| Future standalone NuGet template acquisition, managed-package state, update/uninstall, and archive mounting | Microsoft.TemplateEngine's managed provider, called by future CLI management commands |
| Functions-specific raw constraint validation and eligibility | Separate constraints increment; not implemented |
| Remote package search index and search UX | Separate Functions search/index work, not the local engine cache |

The engine's native scanner validates its template format and filters invalid templates while reporting diagnostics; cached listing is not a fail-closed package-validation API. `WorkloadTemplateSource` checks that declared directories exist; it does not inspect their `template.json` files. There is currently no additional Functions-specific raw declaration validator.

The planned command integration supplies the relevant project or Stack context. The command-facing API should return usable template choices and meaningful reasons when candidates are unavailable. Prefer a straightforward call that accepts context and returns the relevant collection; do not require the CLI to orchestrate multiple discovery passes merely to obtain that list. Whether filtering happens internally during discovery or in a subsequent resolution operation is an API detail for the CLI team to settle.

Template producers can develop project and item content independently. Representative local templates with different Stack metadata should exercise discovery, filtering, and selection. Production content arrives through workload packaging; the .NET workload packages its templates under `content/templates` and declares that directory as `templateRoot`. Test-only installation in an isolated hive does not prove that a packaged workload exposes its templates.

Ordinary listing should exclude hidden items while retaining diagnostics for unavailable candidates, without imposing creation-only bundle gates. Resolve the explicit project target without introducing upward search, and keep selection within the chosen source/channel scope.

### Versioning and duplicate identities

Package versions, template identities, and cache-format versions are separate concepts. Core templates are versioned with their workload; see [Core and independently packaged template versioning](#core-and-independently-packaged-template-versioning). Workload activation chooses the highest RID-compatible runtime version of each package ID before the provider supplies its root; the template engine does not make that workload-version decision.

The repository currently pins `Microsoft.TemplateEngine.*` to **11.0.100**. Current behavior is characterized by isolated real-engine tests against that dependency: `ListAsync_DuplicateIdentity_UsesSourceOrderNotPackageVersion` verifies source-order selection rather than package-version selection, and `ListAsync_ManagedTemplates_UseNativePriorityOverWorkloadTemplates` verifies managed templates override workload templates with the same identity. These tests establish observed behavior, not the current engine's implementation details. Distinct templates should have distinct identities; shared identities are intentional overrides. Ambiguous short names among different identities still need command-level selection.

Historical source analysis against Microsoft.TemplateEngine **10.0.301** found that providers were ordered by priority and the last scanned template with a given identity won; the workload provider had default priority 0 and the built-in managed provider priority 1000. It also found that template `precedence` was not used for cache identity deduplication, and that the managed provider normally kept one installed version of a package identifier per installer: changing versions replaced the previous package and could uninstall it before the new install succeeded. These are findings about 10.0.301, not verified implementation details of the current 11.0.100 dependency.

The tests above cover observed duplicate-identity selection and workload/managed-provider aggregation. This foundation does not introduce public standalone-template install/update/uninstall commands, finalize package-source or version-selection UX, or add a Core Tools duplicate-identity/version-resolution framework.

The historical source analysis above used Microsoft.TemplateEngine **10.0.301**: [cache refresh](https://github.com/dotnet/templating/blob/v10.0.301/src/Microsoft.TemplateEngine.Edge/Settings/TemplatePackageManager.cs), [identity normalization](https://github.com/dotnet/templating/blob/v10.0.301/src/Microsoft.TemplateEngine.Edge/Settings/TemplateCache.cs), [native scanning](https://github.com/dotnet/templating/blob/v10.0.301/src/Microsoft.TemplateEngine.Edge/Settings/Scanner.cs), and [managed-package lifecycle](https://github.com/dotnet/templating/blob/v10.0.301/src/Microsoft.TemplateEngine.Edge/BuiltInManagedProvider/GlobalSettingsTemplatePackageProvider.cs). It is not source-level evidence for the currently pinned 11.0.100. Rerun the focused tests when upgrading the engine.

<a id="parameter-evaluation"></a>
<a id="token-capture"></a>
<a id="exact-id-selection"></a>

## Selection and parameters

General template selection and parameter evaluation remain future work. Template-driven project initialization selects a unique `func-{stack}` default and binds `func.host.json` aliases against the selected template's native parameter definitions. The broader design allows templates to be selected by explicit identifier or interactive choice; selection must preserve the user's choice:

- A full template ID identifies one specific candidate. If that candidate is unavailable or incompatible, report the problem; never substitute a related variant.
- Short names and aliases may identify groups of related templates. Ambiguity must be resolved explicitly, not by an arbitrary first match.
- Project, Stack, language, template kind, and declared constraints determine eligibility. Invalid or unreadable declarations must not silently become successful selections.
- A catalog entry describes a candidate. Only the exact resolved candidate within its valid command scope may be invoked.

For richer selection, group variants only by explicit GroupIdentity; an ungrouped template is an identity-keyed singleton. Within the effective engine catalog, match full identity before aliases or eligibility filtering. Duplicate identities have already been resolved by the native precedence described above; cached listing cannot report every original declaration. An alias matching one variant does not widen to its siblings, and matching display names do not establish a family. Preserve established supported IDs and known aliases; changes to alias, case or precedence policy require agreement. A concrete catalog selection stays pinned to its issued candidate; family selection is explicit.

Functions-specific template applicability rules belong to a separate constraints increment and are not implemented. Where and how they are validated will be decided with that contract. This foundation adds no raw declaration validator and does not change native duplicate-identity precedence.

Constraint-aware selection and invocation will evaluate engine constraints and Functions requirements against one captured context. Failed, restricted, unknown or unevaluated required constraints must block those operations, including with force. Preserve diagnostics rather than treating unreadable or corrupt content as an empty successful selection. The initial constraint-free .NET/HTTP slice does not implement that broader contract.

Template-specific options vary by candidate. The CLI must retain the user's original arguments until it can check their syntax against eligible candidates **before choosing the final variant**. An unknown option's value must not accidentally become the project path. For the initial `func new` grammar, an explicit project path belongs before template-specific options; ambiguous ordering produces guidance rather than silently using the current directory.

The CLI preserves template-specific arguments as an immutable sequence for each command invocation. It retains their original aliases, token positions, and whether values were explicitly supplied. This information is used for candidate parsing and diagnostics; it must not be reconstructed from normalized parameter names.

Before selecting a final variant, the CLI checks the arguments against each eligible candidate. It retains the reason when a candidate is rejected. Candidates with missing values that can be supplied through prompting remain eligible for interactive selection.

The existing CLI grammar remains unchanged. Unsupported `--path`, `--output`, `--dry-run`, and `-name` options retain their current diagnostics, including existing help and `--` behavior. Template aliases must not reinterpret reserved CLI options.

After selection, Microsoft.TemplateEngine evaluates authored parameter semantics, including conditional requirements, enabled or disabled parameters, and dependent defaults. The CLI owns input provenance and prompting, not a second expression evaluator. Preserve the distinction between omitted, explicitly empty, repeated, and explicitly supplied default-equal values. Reject conflicting explicit assignments. Prompt only for active required inputs that remain unresolved; noninteractive calls fail with actionable missing-input diagnostics.

Bind CLI-owned values, including name, and project-derived values to canonical parameter names without allowing template aliases to shadow built-ins. Preserve each value's origin. Invalid explicit input, including a value supplied to a disabled parameter, fails rather than being replaced by a default. After prompting, re-evaluate in the same command-scoped engine and pass the engine-evaluated canonical inputs to invocation, not values reconstructed from projected metadata.

If the installed engine cannot evaluate a required template feature, reject that candidate before changing the destination. The supported parameter-feature subset and exact API need focused qualification.

<a id="selected-content-and-constraints"></a>

## Selected content and rendering

Rendering and selected-content validation are not implemented. The proposed design requires invocation to use the exact content selected, not a later lookup by name or a different variant.

Before a template can be invoked or its parameters prompted for, validate its configuration, metadata, and assets. Reject unsafe paths, unreadable or unsupported content, and assets that exceed the configured size or expansion limits.

The proposed rendering path uses the installed workload content mounted by Microsoft.TemplateEngine directly. It does not revalidate the original authoring source or create a separate immutable rendering snapshot. Missing or unreadable selected content must fail rather than trigger rediscovery or substitution. Concurrent workload changes during a command are not detected.

The exact Microsoft.TemplateEngine mount and invocation API is still to be qualified. Use the existing engine APIs through the command-scoped `Templater` rather than introducing a second engine or a separate lifecycle framework.

Render into command-scoped private temporary output within defined asset and expansion limits. Return bounded planned file bytes, explicit directories and diagnostics; these are not completed destination effects. Preserve the supported func host bindings, but do not execute unapproved custom bind sources. Reject unsupported effects before touching the destination. Rendering failures must not become empty success, and unexpected internal errors remain distinct from invalid user input.

<a id="post-actions"></a>

## Post actions

`PostActionDispatcher` (`src/Func/Templates/PostActions/`) processes a creation result's post actions in declared order. It is not yet wired into `func init` or `func new`.

- Call `templater.ProcessPostActionsAsync(selectedTemplate, creationResult, isDryRun, cancellationToken)` to resolve the dispatcher from the template engine components and run it. Supply the exact selected template, not a rediscovered sibling or ambient project context. `AddTemplating()` registers the dispatcher with dependency injection and the engine.
- Core actions resolve through the engine by `actionId`. Core processors implement the internal `ICorePostActionProcessor` and use `services.AddTemplateComponent<ICorePostActionProcessor, T>()`. No concrete core processors are registered yet.
- `IFuncTemplateComponentProvider.Get()` assembles the engine's full component list. Its DI implementation, `FuncTemplateComponentsProvider`, combines built-in engine components, `ICoreTemplateComponent` registrations, and supported workload bridges. `TemplaterFactory` consumes the provider rather than assembling components itself. Workload registrations retain a CLI-owned `ITemplateComponent`; unsupported contracts are ignored when building bridges.
- Workloads implement the public `IPostActionProcessor : ITemplateComponent` from `Abstractions` and call `builder.AddTemplateComponent<T>()` or the instance overload. The generic overload uses singleton activation through DI; the instance overload leaves disposal with the caller. The CLI adapter projects immutable CLI-owned context types, with no engine types crossing workload assembly load contexts.
- Workload processors retain their registering workload's identity for diagnostics, but do not need to register or own an `IProjectStack`. Each implements synchronous, side-effect-free `CanProcess(PostActionSelectionContext)`. The CLI-owned immutable context exposes the selected template's trimmed `Stack` and `Language` tags, preserving casing. Processors explicitly choose supported values and aliases; installed stack services are not consulted.
- Core processors always take precedence, regardless of registration order, without evaluating workload predicates for a shadowed ID. Workload processors sharing an otherwise-unclaimed ID are grouped behind one engine adapter. The adapter evaluates every candidate's predicate before executing any processor.
- Exactly one workload registration must accept the context. Missing/blank tags are passed as empty strings for workload predicates to reject; no eligible processor and ambiguous matches fail with a diagnostic and manual instructions rather than a registration-order fallback. Conflicts identify the contributing workloads and processor types; repeated registration of the same eligible instance also conflicts. Predicate overlap is detected at dispatch, not registration. A heterogeneous template without one matching stack/language pair does not select an arbitrary workload. This precedence policy is not a security sandbox against workloads modifying the shared service collection.
- A `false` execution result reports failure without trying another processor after potential side effects. Unexpected predicate and execution exceptions propagate, as does cancellation.
- When no processor is found, or a processor returns `false`, the dispatcher reports the failure with the action's description and manual instructions, if any. A failure remains in the aggregate status; `continueOnError` controls whether later actions run.
- Dry runs list the actions that would run without evaluating predicates or executing processors.
- No core run-script processor (`3A7C4B45-1F5D-4A30-959A-51B88E82B5D2`) is implemented. Without a matching workload processor it falls back to manual instructions; its allow, deny, or prompt policy is still to be designed.

See [workload authoring](../building-a-workload.md#template-post-actions) for registration, context, and lifetime details. This API adds no concrete processors or command wiring.

<a id="non-force-init"></a>

## `func init`

`func init` uses the Template Engine when applicable project templates are discovered. The existing Stack initializer continues to serve when no applicable template exists. `InitCommand` parses the request, validates the stack and language against the installed stack descriptors before any route writes, and routes it; `TemplatedInitHandler` owns template creation and the adoption and repair of existing projects; `LegacyInitCommand` owns the existing Stack scaffolding. Template selection uses the template language tag that `TemplateLanguage` maps from the stack's canonical language ID. Initialization fails when candidates exist but no unique `func-{stack}` default is available.

The transitional grammar is `func init <path> [template options]`: an explicit path precedes template and Stack options, based on the original token order. A path after any such option is rejected, whether the option is template-only, Stack-registered, Boolean, or value-taking.

Public parsing is route-neutral for Stack-contributed syntax. Stack options appear in help, but their typed validation runs only on the route that consumes them, so a Stack option cannot reject syntax that the selected template authors differently:

- The template route binds every alias the selected template authors in `func.host.json` according to that template's native parameter definitions, including spellings a Stack also registers. Stack-only aliases the template does not author, and a native parameter assigned more than once, are rejected.
- The existing Stack route parses the same tokens against the Stack's typed options before any change.
- Adoption and repair create no project content; they reject template and Stack options, including registered Stack options, before prompting or writing.

On the template route, creation proceeds in this order: Template Engine dry run, destination preparation, the `.func/config.json` write, native Template Engine creation, Stack setup through `IProjectInitializer.ConfigureProjectAsync`, a cancellation check, then success. Template post-actions aren't run yet; #6023 tracks that. The Stack scaffolding route also writes the config before `InitializeAsync`, and both routes write it only through `IProjectStackConfiguration`. Writing the config first marks the folder as a project, so another `func init` there stops unless it passes `--force`. If the config can't be saved, `func init` exits 1 before any project file is created. If creation or Stack setup fails, the config stays and the folder may hold a partly created project: `func init` exits 1, names what failed, and says to start over with `--force`, which deletes everything in the folder except `.git`. If the project folder can't be created or cleared, `func init` exits 1 without writing a new config. Exceptions that escape CLI-owned operations reach the CLI's unexpected-error handling; Template Engine generation failures come back as a failed result. #6022 tracks keeping the old content until the new project is created. Cancellation after creation does not remove created files or fall back to Stack scaffolding.

The proposed template-based path for creating a project for a selected Stack and language is:

1. Parse the existing `func init` options and inspect the destination.
2. Preserve existing project adoption and configuration-repair behavior without running a project template.
3. For new scaffolding, resolve the Stack, language, and contributed options before any destructive change.
4. Find the default installed project template; validate parameters and selected content.
5. Render privately and combine the output with required Stack and CLI configuration.
6. Validate the complete intended project and authorized file effects, then publish files and persist required configuration last.
7. Report actual changes and failures, including any partial effects.

Without `--force`, an occupied directory that is **not** already a Functions project may retain safe existing scaffold files. Write missing files without overwriting preserved content, and fail if the combined result cannot form a valid project. A complete existing Functions project is not silently reinitialized. Adoption and repair preserve unrelated configuration keys and the existing single-language omission behavior.

Diagnose readable malformed CLI configuration without force. Explicit force may authorize regeneration only after the remaining validation succeeds. Unreadable or unsafe configuration is not treated as replaceable malformed content, and force does not bypass that distinction.

`--force` authorizes only the specified operation after validation and the existing confirmation policy. Protect both `.git` directories and `.git` pointer files. A failed required configuration write must return failure rather than report success. Current `--force` cleanup preserves a `.git` directory but deletes a `.git` pointer file, so it is not yet safe for Git worktrees.

## `func new`

`func new` currently skips first-run setup. Its handler reports that template generation and listing are temporarily unavailable; it does not check whether the target is an initialized Functions project or modify it.

The proposed `func new` path adds a function to an existing, initialized Functions project. If the project has not been initialized, the command should fail with an actionable error directing the user to run `func init` first. It should not initialize the project automatically.

The initial public command surface retains `[path]`, `--template`, `--name`, `--force`, `--non-interactive`, `--list`, and `--format`. The redesign does not add a positional template selector or a new `--path` option.

The proposed path is:

1. Parse built-in options while preserving template-specific arguments.
2. Resolve the target Functions project and its Stack/language context. If the target is not an initialized Functions project, fail before template discovery or any destination changes.
3. Discover installed item templates and show choices or resolve the supplied reference.
4. Check candidate eligibility and parse template-specific arguments for each eligible variant before final selection.
5. Select one exact template, bind its content, and evaluate parameters using the Template Engine.
6. Render privately, validate destination effects, and publish the generated function.
7. Report the actual outcome and dispose the `Templater` to release engine resources.

Listing is not proof that generation works. An exact-ID failure cannot fall back to a sibling. Without force, creating an item fails on a destination collision; any approved modification to existing files requires its own guard. `func new` does not write `func init` configuration or acquire packages during discovery.

## Filesystem and failure behavior

The following describes the proposed template-based flow, not guarantees of the current discovery foundation or existing initializers.

The CLI owns destination safety. Before publishing, it validates relative paths, path aliases, ancestor links, collisions, originals, and the exact authorized force-deletion scope. A replace or append operation must check that its original has not changed, even when force was requested. File publication must be qualified on supported operating systems; an ordinary move operation is not automatically a safe guarded replacement.

Parsing and validation do not create the destination or perform force cleanup. Immediately before each filesystem effect, recheck the relevant permissions, protected ancestry and expected originals; changed originals fail even with force. Run required Stack follow-up only after its prerequisites succeed, and include follow-up failures in the command outcome and actual-effect report.

Check cancellation before irreversible work and between effects. If publication, configuration, setup, or cleanup fails after some changes, report the completed and remaining or uncertain effects. Do not claim all-or-nothing rollback. Distinguish cleanup failures from successful generation.

Report files created, modified, deleted, skipped, and
preserved. When non-force initialization retains existing
scaffold files, identify those files in the command result.
Do not report planned output as completed changes.

<a id="workload-registration"></a>

## Content and workload ownership

Workloads are the delivery mechanism for templates. A workload declares an optional `templateRoot` in `workload.json`, a path relative to its package root. Installation persists it and extracts the package payload. The loader resolves the root for each active runtime workload, and the workload template package provider hands those roots to Microsoft.TemplateEngine on every command. Microsoft.TemplateEngine keeps mounting, scanning and caching; func adds no parallel implementation.

One declared root can contain multiple project and item templates. The engine discovers them recursively; the CLI does not prescribe per-template subdirectory names or register each definition separately. The .NET workload packages its project, HTTP, and Timer templates under `content/templates` and declares that path as `templateRoot`; installed-workload discovery resolves and supplies that root to the engine.

Workload templates require no CLI-hive installation or removal step. Install, update and uninstall change the workload inventory, and the next command uses the resulting active roots. Because the roots come from the resolved workload store, they do not depend on `FUNC_CLI_HOME` matching `FUNC_CLI_WORKLOADS_HOME`.

A package can have several owners. Removing or updating one owner must not delete the payload while another owner retains it; final-owner removal allows the physical package and its templates to be removed together. Retention and discovery are separate: only the highest RID-compatible runtime version of each package ID is selected for loading, and only successfully loaded workloads contribute roots. Older versions retained for rollback do not contribute templates while inactive.

`func init` creates a `Templater` and uses these roots without registering content itself; `func new` is intended to do the same. Do not force a full cache rebuild on every command; keep that for repair or troubleshooting.

`templateRoot` is optional, and workloads installed before it was persisted do not expose templates until they are reinstalled or updated. A declared root whose directory is missing is currently skipped silently. The planned integration should report that the templates are unavailable and explain how to repair the workload; it should not show an empty list as success. Discovery should not scan arbitrary folders or download content.

The existing .NET project-template workflow uses its own `dotnet new` hive, which is separate from workload template roots. Keep current Stack initialization working while package producers migrate content to the format expected by the new engine.

### Core and independently packaged template versioning

Core templates are the basic templates that a Stack workload carries under its `templateRoot`, such as the .NET project template for `func init` and basic .NET item templates, such as HTTP, for `func new`. They are versioned and serviced with that workload and have no separate package version or servicing lifecycle, so updating a core template requires publishing a new workload version.

Additional templates, such as Service Bus, SDK- or service-specific, partner, and quickstart scenarios, or templates that need more frequent updates, can later be delivered as independently versioned template packages: the future standalone packages described below. Once installed, their templates would use the same Microsoft.TemplateEngine discovery and selection path as workload templates, which `func init` and `func new` are intended to consume; commands need no separate path for workload-bundled and independently packaged templates. These packages do not modify a workload's payload, and the native identity precedence described above is not a servicing channel for core templates. Independent packages are not implemented.

### Future standalone template packages

The proposed [`func templates search/install/uninstall` design](package-management.md)
manages independently packaged templates rather than using the historical
`func new install/update/uninstall` command family. No such commands are
implemented. The NuGet catalog, package lifecycle, and CLI integration are later
layers; this documentation alone changes no CLI behavior.

In that proposed implementation, `Templater` would expose engine primitives for
listing and managed package operations. `TemplatePackageService` would
prevalidate packages and orchestrate managed-provider operations, keeping a
temporary backup of the previous package during replacement for recovery. The
engine would own package registration, storage and discovery; the CLI would not
snapshot or rewrite its hive. As with workload operations, template package
operations would not coordinate concurrent CLI processes that share a func
home.

Package acquisition would resolve one feed like workload commands: `--source`,
then `FUNC_CLI_TEMPLATES_SOURCE`, then nuget.org, without reading `NuGet.Config`.
The CLI would download the exact version to a temporary directory and verify it
in-process without requiring the .NET SDK. NuGet would verify the integrity of
signed archives; unsigned packages would be accepted. Before mutation, the CLI
would validate package identity and version, require the `FuncTemplate` package
type and reject workload package types, require valid `func.host.json` metadata
and at least one template identity. NuGet and Microsoft.TemplateEngine would
handle archive path safety and template-format validation. The engine's
managed provider would install the local `.nupkg` into the hive and own
registration and discovery. Because replacement uninstalls the old version
before installing the new one, the CLI would retain a temporary copy of the old
package and use the managed provider to restore it if install or discovery
fails. The engine would record a local NuGet package without `NuGetSource`;
package identity and version alone would determine no-op or replacement
behavior. There would be no separate installer or whole-hive snapshot.

Standalone packages would share the func template hive and discovery path but
remain separately owned from workload-provided roots. Template package uninstall
must not remove workload content. Focused integration tests in the lifecycle
layer must qualify cache refresh during install, replacement, and uninstall
without a separate forced rebuild; those tests are not part of this docs layer.

For a known NuGet package ID, the future CLI command delegates acquisition and lifecycle operations to the engine's built-in managed provider in the same CLI hive. The engine retains and mounts the package archive, discovers its templates, and aggregates them with workload-provided roots. The CLI should not extract standalone packages into the workload store or require a fixed internal template layout.

Managed-provider changes raise `TemplatePackagesChanged`; the engine refreshes its source list and checks/rebuilds the cache on subsequent discovery. Thus both acquisition paths feed one engine catalog, but only the workload installer owns workload payloads. A package can contain multiple templates; one package per authoring repository is the default publishing model, not an engine restriction.

Remote indexing/search reads a CDN-published JSON manifest built by scanning NuGet template packages and indexing individual templates. Search maps matching templates to the owning package ID; the CLI does not search NuGet directly. It is separate from the local cache and is not required for installation by known package ID. The package-management proposal defines Functions package-type validation, allowed install feeds, source/version UX, and failure handling; these requirements must be implemented and qualified before enabling standalone management commands. They are not new infrastructure in this slice.

<a id="normative-authority"></a>

## Scope and compatibility

This document defines the proposed initial contract. Archived task lists are not implementation instructions; the dispositions below describe design targets, not a list of implemented features.

| Historical area | Proposed disposition |
| --- | --- |
| `func new` | Retain item-only scope, explicit `--template`, candidate-specific validation and noninteractive failure. Positional-template syntax, `--path`, `--output`, upward project search and bundle-gated listing are superseded. Public dry-run, lifecycle commands and fallback parameter aliases are deferred. |
| `func init` | Retain existing Stack selection, adoption/repair and configuration behavior. Occupied non-project targets preserve safe files. Public template selection, new noninteractive/dry-run options, template-first UX, a general action framework and removal of working initializers are deferred. |
| Engine integration | Retain host context, engine metadata, native duplicate precedence, func-host aliases and exact invocation. Blanket duplicate-ID rejection, projection-driven conditional evaluation and full-ID widening are superseded. Richer grouping/selection frameworks and authored action plumbing are deferred. |
| Constraints | Raw validation and fail-closed evaluation belong to the separate constraints increment. They do not block the initial constraint-free .NET/HTTP slice. Installer/update preflight and producer-specific rules are deferred with those workflows. |
| Package lifecycle | The proposed `func templates install/uninstall` surface acquires packages over one feed resolved like workload commands, with preflight validation and managed-provider recovery. `func templates search` uses a separate CDN-backed template index, currently represented by an unavailable stub. These commands are not yet available. Standalone packages use the engine's managed provider and existing hive; workload roots retain the read-only workload provider. Replacement safety must be qualified in the lifecycle layer. |
| Quickstarts | The `func quickstart` command has been removed. Template-first, multi-project, topology/action and browse proposals are deferred. |
| Umbrella and producer pipeline | Preserve program and producer history, not a requirement to complete all historical changes before the first engine proof. Producer automation remains deferred. |

The historical research reports `docs/research/historical-design-reconciliation.md` and `docs/research/v4-command-behavior-baseline.md` are unavailable in this checkout, as are historical package bytes and artifact-exact parity evidence. Their citations do not establish parity or executed qualification.

## Decisions to finish with the team

- **CLI team:** the smallest command-facing API and object shapes; template group/variant selection and alias policy; supported parameter semantics and token handoff.
- **Template content owners and workload owners:** approved project/item content for additional Stacks and independently published package-type/feed policy. The .NET workload already packages its core templates under `content/templates` and exposes that path through `templateRoot`. Let the engine discover definitions within each root/archive rather than prescribing their internal layout.
- **CLI team and release owners:** public activation, force-policy changes, release qualification, and Stack expansion beyond the initial .NET/HTTP slice.

Beyond the initial slice, the CLI team and release owners must settle explicit hidden-ID access, profile/no-bundle policy and additional item-Stack scope, including Go, Java and PowerShell. Until approved, these decisions do not change current public behavior or activate unavailable generation.

Additional Stack support can proceed with representative installed content while those production-content decisions are resolved. Public availability requires the appropriate real content, workload packaging, and qualification.
