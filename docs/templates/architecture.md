# Templates Architecture

**Status:** Approved supporting architecture. The [component model](Templates-EndToEnd-Object-Model-Review.md) is the implementation authority; work proceeds through small increments.

## Overview

The CLI uses one **Template Engine** for project and item templates. `func init` selects project templates; `func new` selects item templates. Quickstarts are project templates, not a third template kind.

The six accepted responsibilities are:

- **CLI Commands** own command interaction, authorization, project effects,
  finalization, and user-facing results.
- **Project and Stack Information** provides the resolved project and Stack
  context; the owning Stack supplies Stack-specific compatibility and task
  behavior.
- **Template Engine** uses Microsoft.TemplateEngine for installed-template
  discovery, exact selection, supported parameter processing, and
  template-defined task coordination.
- **Installed Templates** is the common registration boundary for locally
  available content in the CLI Template Engine Hive.
- **Generate Files Privately** creates planned output without modifying the
  target project.
- **Write Files to Project** applies only the changes authorized by CLI
  Commands after private generation succeeds.

For GA, Stack workload integration supplies that Stack's curated starter
templates and ensures they reach Installed Templates before the supported
template experience is reported ready. `func init` and `func new` consume
registered content; neither command acquires its required GA starters.
Production integration is not yet implemented. See the
[packaging and acquisition contract](packaging-and-acquisition.md) for supply,
registration, and future lifecycle policy. The Template Engine remains part of
the CLI for this milestone; physical extraction under issue #5330 remains
deferred.

The eventual representative journey is `func init` creating a project, `func new` adding a function, and the Functions Host discovering it. That end-to-end proof is not a prerequisite for the first object-model foundation and does not, by itself, establish release qualification.

<a id="end-to-end-proof"></a>

## Responsibilities and object model

The command-facing flow is:

1. A command resolves the requested project or Stack and captures the context needed by the Template Engine.
2. `ITemplaterFactory` creates a command-scoped `Templater` from `TemplateEngineContext`.
3. `Templater` lists installed metadata or resolves one exact eligible template.
4. The command handles any interaction and supplies supported inputs.
5. The Template Engine processes supported parameters, generates privately, and coordinates approved template-defined tasks.
6. The command validates and writes authorized files, completes command or Stack work, and reports actual results.

| Component | Responsibility |
| --- | --- |
| CLI Commands | Own public options and interaction, resolve the project or requested Stack, authorize and write destination changes, complete command-specific work, and report results. |
| `TemplateEngineContext` | Capture the command, project, and bundle context used to create `Templater`. Retain this name; it already describes the factory input. |
| `ITemplaterFactory` and `TemplaterFactory` | Create the command-scoped `Templater` without exposing Microsoft.TemplateEngine construction to commands. |
| `Templater` | List installed catalog metadata and resolve an exact template or return actionable diagnostics. It owns the engine session. |
| `TemplateEngineSession` | Internal to `Templater`; owns the Microsoft.TemplateEngine environment and package manager for the command lifetime. Commands do not depend on it. |
| `TemplateCatalogEntry` | Describe installed template metadata and parameters; it does not authorize invocation. |
| `ResolvedTemplate` and `TemplateResolutionResult` | Carry the selected descriptor or candidates and diagnostics; re-resolving a catalog entry is restricted to the `Templater` instance that issued it. |
| Template Engine internals | Use Microsoft.TemplateEngine for metadata, supported parameter semantics, private generation, and approved task orchestration. |
| Template and workload owners | Supply project/item content and Stack-specific tasks; workload integration supplies GA starters and ensures locally available content reaches Installed Templates before readiness. |

Keep Microsoft.TemplateEngine types private to the engine implementation. Use the existing command-facing context and catalog types; do not add a group, request wrapper, task framework, or lifecycle owner without a concrete consumer. Template resolution uses the command-scoped session; only the issuing `Templater` can re-resolve an issued catalog entry. A future invocation must remain in that session and verify the exact selected source before private generation; session ownership does not prove that installed content is unchanged or safe to render.

## Installed templates and discovery

The Template Engine uses Microsoft.TemplateEngine to discover content registered in the dedicated CLI Hive. Discovery does not acquire or install content; supply and registration policy is defined in the [packaging and acquisition contract](packaging-and-acquisition.md).

A command supplies the project or Stack context. `Templater` lists metadata and resolves a requested identity or alias to one exact eligible candidate, returning reasons when candidates are unavailable or a reference is ambiguous. A catalog entry is descriptive metadata, not permission to invoke a template.

Template producers can develop project and item content independently. Representative local templates with different Stack metadata should exercise discovery, filtering, and selection. Production workload integration remains unimplemented; test-only registration in an isolated hive does not prove production readiness.

Listing retains hidden or unusable entries as metadata with diagnostics; resolution rejects hidden or invalid entries. Listing does not impose creation-only bundle gates. Resolve the explicit project target without introducing upward search, and keep selection within the selected source scope.

<a id="parameter-evaluation"></a>
<a id="token-capture"></a>
<a id="exact-id-selection"></a>

## Selection and parameters

A template can be selected by an explicit identifier or through an interactive choice. Selection must preserve the user's choice:

- A full template ID identifies one specific candidate. If that candidate is unavailable or incompatible, report the problem; never substitute a related variant.
- Short names and aliases may identify groups of related templates. Ambiguity must be resolved explicitly, not by an arbitrary first match.
- Project, Stack, language, template kind, and declared constraints determine eligibility. Invalid or unreadable declarations must not silently become successful selections.
- A catalog entry describes a candidate. Only the exact resolved candidate within its valid session may be invoked.

The foundation returns `Selected`, `NotFound`, `Ambiguous`, or `Failed`. An ambiguous result contains the eligible candidates; the caller must choose explicitly. Resolution does not prompt or select a candidate automatically, and failures retain their diagnostics.

When variants are grouped, use only an explicit GroupIdentity; an ungrouped template is an identity-keyed singleton. The foundation does not need grouping to report an ambiguous alias. Within the selected source scope, match full identity before aliases or eligibility filtering. Duplicate applicable full IDs are an error. An alias matching one variant does not widen to its siblings, and matching display names do not establish a family. Preserve established supported IDs and known aliases; changes to alias, case or precedence policy require agreement. A concrete catalog selection stays pinned to its issued candidate; family selection is explicit.

Validate raw mounted template declarations before engine normalization can discard invalid or duplicate requirements. Evaluate engine constraints and func workload/bundle requirements against one captured context. Failed, restricted, unknown or unevaluated required constraints block selection and invocation, including with force. Preserve their diagnostics. Report unavailable returned mounts and missing, unreadable, or malformed template configurations when observed; Microsoft.TemplateEngine may suppress provider-enumeration failures without a typed completeness result, so `NotFound` does not prove the installed catalog was completely enumerated. See the [implementation limitation](implementation.md#pr-1-foundation).

Template-specific options vary by candidate. The CLI must retain the user's original arguments until it can check their syntax against eligible candidates **before choosing the final variant**. An unknown option's value must not accidentally become the project path. For the initial `func new` grammar, an explicit project path belongs before template-specific options; ambiguous ordering produces guidance rather than silently using the current directory.

The CLI preserves template-specific arguments as an immutable sequence for each command invocation. It retains their original aliases, token positions, and whether values were explicitly supplied. This information is used for candidate parsing and diagnostics; it must not be reconstructed from normalized parameter names.

Before selecting a final variant, the CLI checks the arguments against each eligible candidate. It retains the reason when a candidate is rejected. Candidates with missing values that can be supplied through prompting remain eligible for interactive selection.

The existing CLI grammar remains unchanged. Unsupported `--path`, `--output`, `--dry-run`, and `-name` options retain their current diagnostics, including existing help and `--` behavior. Template aliases must not reinterpret reserved CLI options.

After selection, Microsoft.TemplateEngine evaluates authored parameter semantics, including conditional requirements, enabled or disabled parameters, and dependent defaults. The CLI owns input provenance and prompting, not a second expression evaluator. Preserve the distinction between omitted, explicitly empty, repeated, and explicitly supplied default-equal values. Reject conflicting explicit assignments. Prompt only for active required inputs that remain unresolved; noninteractive calls fail with actionable missing-input diagnostics.

Bind CLI-owned values, including name, and project-derived values to canonical parameter names without allowing template aliases to shadow built-ins. Preserve each value's origin. Invalid explicit input, including a value supplied to a disabled parameter, fails rather than being replaced by a default. After prompting, re-evaluate in the same session and pass the engine-evaluated canonical inputs to invocation, not values reconstructed from projected metadata.

If the installed engine cannot evaluate a required template feature, reject that candidate before changing the destination. The supported parameter-feature subset and exact API need focused qualification.

<a id="selected-content-and-constraints"></a>

## Generate Files Privately

Invoking a template must use the exact content selected, not a later lookup by name or a different variant.

Before a template can be invoked or its parameters prompted for, validate its configuration, metadata, and assets. Reject unsafe paths, unreadable or unsupported content, and assets that exceed the configured size or expansion limits.

At selection time, record the installed package and mount identity and a deterministic manifest of the selected assets. The manifest includes relative paths and content bytes or digests, raw template configuration, and func-host metadata.

Mutable content must be copied and verified at selection time. Rendering must use that verified copy. An installed mount may be used directly only when its immutability and integrity are enforceable.

Before rendering, verify that the selected source still matches the recorded content. Missing, changed, unreadable, or unsafe content invalidates the selection. Do not rediscover or substitute another template.

The exact Microsoft.TemplateEngine mount and invocation configuration still needs production qualification; the separate fixture probe does not establish general support. Use the existing engine and session facilities rather than introducing a second engine or a separate lifecycle framework.

Render into session-owned private temporary output within defined asset and expansion limits. Return bounded planned file bytes, explicit directories and diagnostics; these are not completed destination effects. Preserve the supported func host bindings, but do not execute authored post-actions or unapproved custom bind sources. Reject unsupported effects before touching the destination. Rendering failures must not become empty success, and unexpected internal errors remain distinct from invalid user input.

<a id="non-force-init"></a>

## `func init`

`func init` creates a Functions project for a selected Stack and language. Existing initializers continue to serve unmigrated Stacks until template-based replacements pass qualification.

The proposed template-based path is:

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

The intended `func new` journey adds a function to an existing, initialized Functions project.

The proposed CLI `func new` path requires an initialized Functions project. If the project is confirmed to be uninitialized, the command must fail with an actionable error directing the user to run `func init` first. It must not initialize the project automatically.

An unsuccessful project resolution is not evidence that initialization is required. Report ambiguity for conflicting project matches, inspection/access failures for inaccessible targets, configuration errors for corrupt or malformed configuration, and the corresponding unsupported-project or incompatibility diagnostic. Preserve the existing resolution message when the cause is undetermined; do not replace it with `func init` guidance. Project recognition remains the resolver's responsibility, compatibility remains workload-owned, and the command owns diagnostic presentation.

The initial public command surface retains `[path]`, `--template`, `--name`, `--force`, `--non-interactive`, `--list`, and `--format`. The redesign does not add a positional template selector or a new `--path` option.

The proposed path is:

1. Parse built-in options while preserving template-specific arguments.
2. Resolve the target Functions project and its Stack/language context. If resolution or inspection fails, report the cause before template discovery or any destination changes; offer `func init` guidance only for a confirmed uninitialized target. After resolution, reject unsupported or incompatible projects with the corresponding diagnostic before generation or file changes.
3. Discover installed item templates and show choices or resolve the supplied reference.
4. Check candidate eligibility and parse template-specific arguments for each eligible variant before final selection.
5. Select one exact template, bind its content, and evaluate parameters using the Template Engine.
6. Render privately, validate destination effects, and publish the generated function.
7. Report the actual outcome and release session resources.

Listing is not proof that generation works. An exact-ID failure cannot fall back to a sibling. Without force, creating an item fails on a destination collision; any approved modification to existing files requires its own guard. `func new` does not write `func init` configuration or acquire packages during discovery.

`func new` skips first-run setup. The current handler reports template generation and listing unavailable without modifying the destination. The current command implementation and the proposed generation path must be distinguished during integration and testing.

## Write Files to Project

The CLI owns destination safety. Before publishing, it validates relative paths, path aliases, ancestor links, collisions, originals, and the exact authorized force-deletion scope. A replace or append operation must check that its original has not changed, even when force was requested. File publication must be qualified on supported operating systems; an ordinary move operation is not automatically a safe guarded replacement.

Parsing and validation do not create the destination or perform force cleanup. Immediately before each filesystem effect, recheck the relevant permissions, protected ancestry and expected originals; changed originals fail even with force. Run required Stack follow-up only after its prerequisites succeed, and include follow-up failures in the command outcome and actual-effect report.

Check cancellation before irreversible work and between effects. If publication, configuration, setup, or cleanup fails after some changes, report the completed and remaining or uncertain effects. Do not claim all-or-nothing rollback. Distinguish cleanup failures from successful generation.

Report files created, modified, deleted, skipped, and
preserved. When non-force initialization retains existing
scaffold files, identify those files in the command result.
Do not report planned output as completed changes.

<a id="workload-registration"></a>

## Content and workload ownership

The [packaging and acquisition contract](packaging-and-acquisition.md) defines the approved GA workload supply and Installed Templates registration responsibility and the future lifecycle boundary. The production integration is not implemented; current infrastructure and local test registration do not establish that the supported workload experience is ready.

The existing .NET project-template workflow uses its own template hive. That is not evidence that production workload content is already registered in the new CLI hive. Keep current Stack initialization working while content owners prepare templates for the new engine.

**Implementation status:** The GA workload supply and registration requirement is approved but not implemented. See the [implementation status](implementation.md) for remaining qualification work.

## Node starter content

The first production content contribution remains one Node.js JavaScript project template and one Node.js JavaScript HTTP item template for Node.js programming model v4. The package, supply, registration, and lifecycle requirements are defined by the [packaging and acquisition contract](packaging-and-acquisition.md).

**Established:** Use Microsoft.TemplateEngine `template.json` metadata with `tags.type` set to `project` or `item` and `tags.language` set to the canonical `JavaScript` value. The canonical Node Stack identifier is `node`. The owning workload supplies Stack compatibility; model-v4 compatibility means the Node.js programming model and `@azure/functions` v4 APIs, not Azure Functions Runtime v4. The HTTP item must use the model-v4 `app.http()` programming model. The two starter templates are visible; omit `func.host.json` `isHidden` or set it to `false`.

**Proposed command/content contract:** Bind the built-in string parameter `name` to the generated function name and use `sourceName` for source substitution; do not define a second function-name option. The HTTP request query value `name` is runtime input, not a template parameter. The command owns any missing-name prompt or default. Fixture qualification covered name-only binding, not general parameter processing. Keep the first content pair to unconditional parameter values: the current implementation does not evaluate conditional requiredness, disabled parameters, or conditional defaults.

Each template has its own content-relative root, with `.template.config/template.json` and all source assets beneath that root. Keep configuration and assets within the content, avoid external or additional configuration files, and verify the selected configuration and assets before invocation as required by [Generate Files Privately](#selected-content-and-constraints). The content must not use authored post-actions or unsupported custom operations. Template metadata and identity requirements are defined by the [packaging and acquisition contract](packaging-and-acquisition.md); production template identities remain unapproved.

The Template Engine classifies templates by type and language; the Node workload must enforce Stack compatibility and verify that an existing project supports programming model v4 before offering the HTTP item. The current `NodeProjectFactory` detects an `@azure/functions` dependency but does not check its version, so model-v4 admission remains implementation work. Do not add a `func-workload` constraint to these templates yet: the PR 1 `TemplateEngineContext` contains no workload inventory and selection fails closed when that constraint cannot be evaluated.

<a id="normative-authority"></a>

## Scope and compatibility

This document supports the approved [component model](Templates-EndToEnd-Object-Model-Review.md), which is the implementation authority. Archived task lists are historical context, not implementation instructions or additional sources of current requirements.

| Historical area | Disposition for this increment |
| --- | --- |
| `func new` | Retain item-only scope, explicit `--template`, candidate-specific validation and noninteractive failure. Positional-template syntax, `--path`, `--output`, upward project search and bundle-gated listing are superseded. Public dry-run, lifecycle commands and fallback parameter aliases are deferred. |
| `func init` | Retain existing Stack selection, adoption/repair and configuration behavior. Occupied non-project targets preserve safe files. Public template selection, new noninteractive/dry-run options, template-first UX, a general action framework and removal of working initializers are deferred. |
| Engine integration | Retain host context, catalog metadata, explicit group identity, func-host aliases, fail-closed eligibility and exact invocation. Projection-driven conditional evaluation and full-ID widening are superseded. General selection frameworks and authored action plumbing are deferred. |
| Constraints | Raw constraint validation and fail-closed constraint evaluation are foundation behavior. Selected-content checks are required before future invocation; installer/update preflight and producer-specific rules are deferred with those workflows. |
| Package lifecycle | GA Stack workload supply and registration are required but not implemented. Search, acquisition UX, update/uninstall, servicing, and replacement behavior remain deferred or owner decisions as defined in the package contract. |
| Quickstarts | Quickstarts are project templates, not a third template kind. Keep the existing route until its Template Engine replacement is implemented and qualified. |
| Umbrella and producer pipeline | Preserve program and producer history, not a requirement to complete all historical changes before the first engine proof. Producer automation remains deferred. |

The historical research reports `docs/research/historical-design-reconciliation.md` and `docs/research/v4-command-behavior-baseline.md` are unavailable in this checkout, as are historical package bytes and artifact-exact parity evidence. Their citations do not establish parity or executed qualification.

## Remaining implementation and qualification

The architecture is settled; the following work remains before template-backed commands can be activated:

- Implement the approved GA workload integration that supplies starter content and registers it in Installed Templates before reporting the template experience ready; qualify content identity, provenance, and integrity.
- Verify selected content before invocation; reject unsupported template effects before destination changes.
- Generate privately, validate the complete output, and publish only guarded, authorized changes with truthful partial-effect reporting.
- Integrate `func init` and `func new` without changing their public grammar or replacing existing Stack behavior before replacements qualify.
- Qualify supported Stack content, Functions Host discovery, cancellation, cleanup, and guarded publication on supported platforms.

The first foundation PR does not require the complete Node.js end-to-end journey. Current initializers and command behavior remain in place until their replacements are integrated and qualified.
