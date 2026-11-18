# Templates Architecture

**Status:** Proposed

## Overview

The proposed Template Engine is intended to let `func init` create a Functions project and `func new` add a function using installed templates. When complete, it will discover templates, use their metadata to find suitable choices, evaluate template parameters, and invoke the selected template.

The initial goal is a working local journey: `func init` creates a valid project; `func new` adds a parameterized function; the Functions Host discovers that function. A small set of representative templates can establish this integration before the complete template catalog is migrated. This is not, by itself, release qualification.

## Current foundation

The current code provides a command-scoped `Templater` with an isolated settings hive. `Templater.ListAsync` returns the engine's `ITemplateInfo` values in a `TemplateResolutionResult`, so optional engine interfaces and `HostConfigPlace` stay available to callers.

Neither `func init` nor `func new` uses this discovery API yet. A workload can declare a `templateRoot` in `workload.json`. The workload store persists it and hydrates the package root, and a workload template package provider exposes active runtime workloads' roots without installing them in the CLI hive. Workload packaging does not emit `templateRoot` yet. The current foundation does not read host-specific configuration, filter or select templates, evaluate constraints or parameters, render output, or write generated files. The rest of this document describes proposed work beyond discovery.

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

The proposed CLI flow captures the command, project, and bundle context once. The current `Templater` accepts a context and passes it and the DI-registered engine components to the func host. Its `ListAsync` method lists installed templates. Parameter evaluation and invocation are not implemented yet.

`TemplateResolutionResult` currently holds the discovered `ITemplateInfo` candidates; it does not group or select them. Add a func-specific projection only when a command needs one. Catalogs, groups, and resolved templates remain design concepts for later work, not current types.

For later selection, the proposed design calls for immutable catalog and group views and one canonical representation of parameter metadata. Only an exact selection issued by the `Templater` should be invoked. Selections from another command scope, or selections that are stale or disposed, must fail.

Template invocation and its temporary rendering resources are not implemented. Define their ownership when invocation is added.

These are responsibilities and useful object-model concepts, not a requirement to introduce a public class or interface for each row. Reuse existing types where they fit. The proposed group, resolution, request, and result shapes inform the command-facing API; their exact signatures still need agreement against concrete command calls.

**Terminology:** A *Stack* is an Azure Functions language stack, such as Node.js or Python. *Runtime* means the Azure Functions application/Functions Host runtime, not a language worker. A `hive` is an isolated scope of installed templates. There is one component named **Template Engine**; the previous unshipped implementation is not the design to reproduce.

## Installed templates and discovery

`Templater.ListAsync` currently lists the templates found in the roots that active workloads declare, plus any packages installed in the isolated CLI hive. The API does not provide template registration or installation, and no CLI command uses it yet.

In the proposed flow, `func init` and `func new` create a `Templater` and read the workload roots through it. The CLI loads the active workload inventory at startup, and each root carries a `LastChangeTime`: the later of the template directory and `workload.json` write times. Microsoft.TemplateEngine uses that timestamp as its rescan signal, including for same-version reinstalls when the timestamp changes. A workload that is no longer active contributes no root, and a root whose directory is missing is skipped.

The provider does not watch for changes during a command. These timestamps are not content hashes and do not detect every in-place edit. End-to-end cache removal after a root disappears and cache refresh after reinstall still need qualification; listing alone does not establish that cached content is usable.

The planned command integration supplies the relevant project or Stack context. The command-facing API should return usable template choices and meaningful reasons when candidates are unavailable. Prefer a straightforward call that accepts context and returns the relevant collection; do not require the CLI to orchestrate multiple discovery passes merely to obtain that list. Whether filtering happens internally during discovery or in a subsequent resolution operation is an API detail for the CLI team to settle.

Template producers can develop project and item content independently. Representative local templates with different Stack metadata should exercise discovery, filtering, and selection. Production content arrives through workload packaging, which does not emit `templateRoot` yet: test-only installation in an isolated hive does not prove that a packaged workload exposes its templates.

Ordinary listing should exclude hidden items while retaining diagnostics for unavailable candidates, without imposing creation-only bundle gates. Resolve the explicit project target without introducing upward search, and keep selection within the chosen source/channel scope.

<a id="parameter-evaluation"></a>
<a id="token-capture"></a>
<a id="exact-id-selection"></a>

## Selection and parameters

Selection and parameter evaluation are not implemented. The proposed design allows a template to be selected by an explicit identifier or through an interactive choice. Selection must preserve the user's choice:

- A full template ID identifies one specific candidate. If that candidate is unavailable or incompatible, report the problem; never substitute a related variant.
- Short names and aliases may identify groups of related templates. Ambiguity must be resolved explicitly, not by an arbitrary first match.
- Project, Stack, language, template kind, and declared constraints determine eligibility. Invalid or unreadable declarations must not silently become successful selections.
- A catalog entry describes a candidate. Only the exact resolved candidate within its valid command scope may be invoked.

Group variants only by explicit GroupIdentity; an ungrouped template is an identity-keyed singleton. Within the selected source scope, match full identity before aliases or eligibility filtering. Duplicate applicable full IDs are an error. An alias matching one variant does not widen to its siblings, and matching display names do not establish a family. Preserve established supported IDs and known aliases; changes to alias, case or precedence policy require agreement. A concrete catalog selection stays pinned to its issued candidate; family selection is explicit.

Validate raw mounted template declarations before engine normalization can discard invalid or duplicate requirements. `Templater.ListAsync` returns the engine's normalized results, in which duplicate identities can collapse, so it cannot enforce this on its own. This validation is not part of the discovery foundation; the constraints work decides where the roots are validated. Evaluate engine constraints and func workload/bundle requirements against one captured context. Failed, restricted, unknown or unevaluated required constraints block selection and invocation, including with force. Preserve their diagnostics; unreadable or corrupt installed content is not an empty-success catalog.

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

Workload installation owns the template payload; the engine and template commands only read it. The proposed rendering path reads the selected template from that root directly, without a private source snapshot, content manifest or duplicate verification pass. Missing or unreadable selected content must fail rather than trigger rediscovery or substitution. This ownership model is not a filesystem lock: detecting concurrent in-place replacement is not implemented, and that behavior needs qualification when invocation is added.

The exact Microsoft.TemplateEngine mount and invocation API is still to be qualified. Use the existing engine APIs through the command-scoped `Templater` rather than introducing a second engine or a separate lifecycle framework.

Render into command-scoped private temporary output within defined asset and expansion limits. Return bounded planned file bytes, explicit directories and diagnostics; these are not completed destination effects. Preserve the supported func host bindings, but do not execute authored post-actions or unapproved custom bind sources. Reject unsupported effects before touching the destination. Rendering failures must not become empty success, and unexpected internal errors remain distinct from invalid user input.

<a id="non-force-init"></a>

## `func init`

`func init` currently uses the existing Stack initializers, not this Template Engine. Existing initializers continue to serve unmigrated Stacks until template-based replacements pass qualification.

The proposed template-based path for creating a project for a selected Stack and language is:

1. Parse the existing `func init` options and inspect the destination.
2. Preserve existing project adoption and configuration-repair behavior without running a project template.
3. For new scaffolding, resolve the Stack, language, and contributed options before any destructive change.
4. Find the appropriate installed project template; validate eligibility, parameters, and selected content.
5. Render privately and combine the output with required Stack and CLI configuration.
6. Validate the complete intended project and authorized file effects, then publish files and persist required configuration last.
7. Report actual changes and failures, including any partial effects.

Without `--force`, an occupied directory that is **not** already a Functions project may retain safe existing scaffold files. Write missing files without overwriting preserved content, and fail if the combined result cannot form a valid project. A complete existing Functions project is not silently reinitialized. Adoption and repair preserve unrelated configuration keys and the existing single-language omission behavior.

Diagnose readable malformed CLI configuration without force. Explicit force may authorize regeneration only after the remaining validation succeeds. Unreadable or unsafe configuration is not treated as replaceable malformed content, and force does not bypass that distinction.

`--force` authorizes only the specified operation after validation and the existing confirmation policy. Protect both `.git` directories and `.git` pointer files. A failed required configuration write must return failure rather than report success.

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

Workload templates require no CLI-hive installation or removal step. Install, update and uninstall change the workload inventory, and the next command uses the resulting active roots. Because the roots come from the resolved workload store, they do not depend on `FUNC_CLI_HOME` matching `FUNC_CLI_WORKLOADS_HOME`.

A package can have several owners. Removing one owner must not delete the payload while another owner retains it; final-owner removal allows the physical package and its templates to be removed together. Retention and discovery are separate: only the highest RID-compatible runtime version of each package ID is selected for loading, and only successfully loaded workloads contribute roots. Older versions retained for rollback do not contribute templates while inactive.

In the proposed flow, `func init` and `func new` create a `Templater` and use these roots without registering content themselves. Do not force a full cache rebuild on every command; keep that for repair or troubleshooting.

`templateRoot` is optional, and workloads installed before it was persisted do not expose templates until they are reinstalled or updated. A declared root whose directory is missing is currently skipped silently. The planned integration should report that the templates are unavailable and explain how to repair the workload; it should not show an empty list as success. Discovery should not scan arbitrary folders or download content.

The existing .NET project-template workflow uses its own `dotnet new` hive, which is separate from workload template roots. Keep current Stack initialization working while package producers migrate content to the format expected by the new engine.

How packaging emits `templateRoot`, version and channel rules, and integrity checks still need agreement between the CLI team, template content owners, and workload owners.

<a id="normative-authority"></a>

## Scope and compatibility

This document defines the proposed initial contract. Archived task lists are not implementation instructions; the dispositions below describe design targets, not a list of implemented features.

| Historical area | Proposed disposition |
| --- | --- |
| `func new` | Retain item-only scope, explicit `--template`, candidate-specific validation and noninteractive failure. Positional-template syntax, `--path`, `--output`, upward project search and bundle-gated listing are superseded. Public dry-run, lifecycle commands and fallback parameter aliases are deferred. |
| `func init` | Retain existing Stack selection, adoption/repair and configuration behavior. Occupied non-project targets preserve safe files. Public template selection, new noninteractive/dry-run options, template-first UX, a general action framework and removal of working initializers are deferred. |
| Engine integration | Retain host context, catalog metadata, explicit group identity, func-host aliases, fail-closed eligibility and exact invocation. Projection-driven conditional evaluation and full-ID widening are superseded. General selection frameworks and authored action plumbing are deferred. |
| Constraints | Raw validation, fail-closed evaluation and selected-content checks are proposed for selection and invocation; they are not implemented in the discovery foundation. Installer/update preflight and producer-specific rules are deferred with those workflows. |
| Package lifecycle | User-facing template package install/update/uninstall/search and feed UX are deferred. Package provenance and failure-safe replacement remain requirements to revisit when those workflows enter scope; workload templates reach the engine through the workload template package provider, not through a separate TemplateEngine package installation. |
| Quickstarts | Preserve the existing Quickstart route. Template-first, multi-project, topology/action and browse proposals are deferred. |
| Umbrella and producer pipeline | Preserve program and producer history, not a requirement to complete all historical changes before the first engine proof. Producer automation remains deferred. |

The historical research reports `docs/research/historical-design-reconciliation.md` and `docs/research/v4-command-behavior-baseline.md` are unavailable in this checkout, as are historical package bytes and artifact-exact parity evidence. Their citations do not establish parity or executed qualification.

## Decisions to finish with the team

- **CLI team:** the smallest command-facing API and object shapes; template group/variant selection and alias policy; supported parameter semantics and token handoff.
- **Template content owners and workload owners:** representative project/item packages, the template folder layout inside a workload and how packaging emits `templateRoot`, versioning, and content integrity.
- **CLI team and release owners:** initial supported Stack scope, public activation, force-policy changes, and release qualification.

The CLI team and release owners must settle explicit hidden-ID access, profile/no-bundle policy and supported item-Stack scope, including Go, Java and PowerShell. Until approved, these decisions do not change current public behavior or activate unavailable generation.

The first implementation can proceed with representative installed content while these production-content decisions are resolved. Public availability requires the appropriate real content, packaging, and qualification.
