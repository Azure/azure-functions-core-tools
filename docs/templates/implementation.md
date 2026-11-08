# Implementing the Template Engine

**Status:** Incremental implementation plan. The [component model](Templates-EndToEnd-Object-Model-Review.md) is the implementation authority; the [architecture](architecture.md) supplies supporting design.

This document distinguishes the PR 1 foundation from deferred rendering, command integration, and qualification. PR 1 does not require the complete `func init` / `func new` / Functions Host journey.

## Existing committed infrastructure

**IMPLEMENTED:** The existing object model includes `ITemplaterFactory`, `TemplateEngineContext`, `TemplateEngineSession`, `Templater`, `TemplateCatalogEntry`, and `TemplateParameterDefinition`. The host registers the factory, and the engine session uses the dedicated CLI template Hive. Catalog and parameter projectors, func-host metadata readers, and constraint helpers provide reusable metadata handling. Commands do not yet call the factory.

## PR 1 foundation

**IMPLEMENTED:** The PR 1 foundation provides a command-facing creation context, factory, command-scoped `Templater`, installed catalog metadata, and exact resolution with actionable diagnostics. `TemplateEngineSession` remains internal to `Templater` and owns the Microsoft.TemplateEngine environment and installed-template package manager for the command. Only the issuing live `Templater` can re-resolve an issued catalog entry; the selected descriptor does not authorize invocation or verify that installed content is unchanged. The implementation reuses `TemplateEngineContext`, `TemplateCatalogEntry`, and existing metadata/constraint helpers; it adds no group model or general task framework. The first Node starter-content scope is described in the [architecture](architecture.md#node-starter-content); package and lifecycle requirements are defined by the [packaging and acquisition contract](packaging-and-acquisition.md).

PR 1 does not change `InitCommand` or `NewCommand`, activate template listing or generation, or replace existing Stack initializers.

Resolution reports unavailable registered mounts and malformed template configurations rather than treating those scans as complete. The underlying `TemplatePackageManager.GetTemplatePackagesAsync` contract can suppress a provider-enumeration exception, log it, and return packages from providers that succeeded. The current host uses a null logger, and the available log record is not a supported, typed completeness result. The foundation therefore cannot reliably distinguish every suppressed provider failure from genuine absence. An incomplete catalog can appear empty, and `NotFound` does not always prove that all providers were available.

A focused observation exercised this boundary with the existing CLI hive: after a template was installed and selected successfully, corrupting `packages.json` left the template files present while listing returned no entries and resolution returned `NotFound`. This observes the existing failure mode; it is not a passing correctness regression. The foundation must not parse localized log text, enumerate providers again, or maintain a second discovery mechanism to infer completeness. Production command activation remains subject to provider-availability and failure-handling qualification.

## Deferred work

- **NOT WIRED:** The GA workload integration must supply each Stack's curated starter templates and ensure they are registered through Installed Templates before the supported template experience is reported ready. Local fixtures can be registered in isolated hives; they do not prove production workload supply or registration. The exact repository mechanism must be verified and reused. Production template identities, package identity if a separate package is chosen, and version/servicing policy remain owner decisions.
- A fixture-specific private-rendering prototype has been developed separately, including bounded source/output handling and planned output. It is not part of this PR, is not wired through the normal factory or a command, and has not been built or tested.
- Installed-content snapshot verification, general supported parameter processing, Stack-owned task contributions, guarded destination publication, and actual-effect reporting remain later work.
- `InitCommand` and its six existing Stack initializers continue to handle project creation, adoption, and configuration repair until replacements qualify.
- `NewCommand` retains its existing public argument surface. Its current migration handler reports listing and scaffolding unavailable before changing the target; PR 1 does not alter that behavior.
- Quickstarts are project templates in the approved architecture. The existing Quickstart route remains until its migration is implemented and qualified.
- The Template Engine remains part of the CLI for this milestone. Physical extraction under issue #5330 remains deferred.

### Existing initialization limitations

The current `func init` path has known safety limitations: it can create the destination before validation, perform force cleanup before all Stack-specific checks, remove `.git` pointer files, and report success after some required configuration writes fail.

These behaviors belong to the existing implementation. The proposed template-based path must correct them before replacing an initializer.

Useful source entry points include `src/Func/Templates/Engine/`, `src/Func/Commands/InitCommand.cs`, `src/Func/Commands/NewCommand.cs`, `src/Abstractions/Projects/`, and `src/Workloads/Stacks/`. Current source and tests define the implemented compatibility behavior.

## Incremental delivery

1. **PR 1 — establish the object-model foundation.** Create `Templater` from the existing command context and factory; list installed template metadata; resolve one exact eligible selection or return diagnostics. Keep the engine session internal and leave commands unchanged.
2. **Supply and register GA starters.** Use the existing workload install/enablement infrastructure to make each Stack's curated starter templates locally available and register them through Installed Templates before reporting the template experience ready. Verify the selected source before invocation; physical delivery must not change discovery behavior.
3. **Qualify private generation.** Use representative project and item content to validate exact invocation, supported parameter processing, bounded private output, cancellation, and approved template-defined tasks. Stack-specific tasks come from the owning workload; do not introduce a generic task framework.
4. **Add guarded publication.** Validate all intended destination effects, protect existing and Git metadata, preserve non-force behavior, and report partial effects truthfully.
5. **Integrate commands incrementally.** Preserve `func init` adoption/repair and current Stack behavior; add project-template and item-template paths without changing public grammar until each path is ready.
6. **Qualify production content and runtime behavior.** Verify production workload registration, Functions Host discovery, supported platforms, and the approved Stack scope before activating public paths.

These increments keep the first PR reviewable. The complete Node.js end-to-end journey is a later integration milestone, not a prerequisite for PR 1.

## Stack behavior to preserve

The six existing initializers provide the compatibility baseline. The exact supported combinations and defaults must be checked against current source and tests before replacing an initializer.

| Stack | Current project languages and important options |
| --- | --- |
| .NET | C# and F#; `--target-framework` defaults to `net10.0` in the documented baseline. `net11.0` is a C# case; existing .NET templates use a separate `dotnet new` hive. |
| Node.js | JavaScript and TypeScript; bundle options and `--skip-npm-install` (default false). |
| Python | Python (`py` alias); bundle options and the current Python project scaffold. |
| Go | Go (`golang` alias; existing worker identifier `native`); bundle options and `--skip-go-mod-tidy` (default false). |
| Java | Java; bundle options and Maven-based scaffolding. |
| PowerShell | PowerShell (`pwsh`, `ps` aliases); bundle options, `--managed-dependencies` (default false), and `--runtime-version` (documented default 7.4, with 7.4/7.6 options). |

Common bundle options include `--no-bundles` and `--bundles-channel`. Preserve Stack/language alias handling, contributed defaults, interactive language selection for multi-language Stacks, and single-language configuration omission. The archived [Stack inventory](archive/func-init-execution/stack-inventory.md) provides detailed historical evidence; current source determines implemented behavior.

Keep the distinction between the language Stack and the Azure Functions Runtime. Do not interpret a worker alias or a public `--runtime-version` option as a new architecture term.

## Remaining implementation gates

| Area | Needed before |
| --- | --- |
| GA workload starter supply and registration | Required starter templates are locally available and registered through Installed Templates before the supported template experience is reported ready |
| Verified source snapshot and supported parameter semantics | Invoking selected installed content |
| Approved template-defined task contracts | Coordinating tasks contributed by a Stack |
| Guarded destination publication | Writing generated files to a project |
| Supported content, Functions Host, and platform qualification | Activating public template-backed command paths |

Engine work with isolated representative content does not need to wait for the entire production catalog. A public template-backed journey does need approved content and production workload supply and registration.

## Focused tests and later acceptance

### PR 1 tests

PR 1 covers factory creation, catalog listing, exact identity selection, alias ambiguity, duplicate identities, ineligible candidates, selection ownership, invalid selection, and actionable diagnostics. At foundation commit `fe9a0d9861f02596c68ad64c567e3ec0b16bad15`, the focused filter ran against an isolated snapshot with .NET SDK 10.0.403: 108 passed, 0 failed, and 0 skipped. That run does not cover subsequent review changes. A separate foundation-only snapshot with the local review corrections passed 52 `TemplaterTests` and `TemplaterFactoryTests`: 0 failed and 0 skipped. It does not include the deferred renderer.

### Later end-to-end journey

Use a real project template and a parameterized HTTP-function template for one supported Stack:

1. Install both into an isolated CLI hive.
2. Run `func init` and verify the resulting project with the normal project resolver.
3. Run `func new` against that project, passing a required parameter.
4. Verify the generated function uses the supplied parameter.
5. Start the approved Functions Host and verify actual function discovery and invocation.

A `host.json` file, successful listing, or basic text-file fixture alone does not establish this proof. One representative Stack does not establish GA coverage.

### Focused correctness cases

- **Discovery and selection:** missing content, multiple metadata variants, ambiguous aliases, ineligible exact IDs, duplicate identities, unavailable constraints, and no sibling substitution.
- **Parameters:** candidate-specific options before final selection; conditional required/disabled/default values; explicit empty and repeated values; interactive and noninteractive errors.
- **Content integrity:** changed or missing configuration/assets between selection and invocation; rendering from the verified selected content; unsupported engine features fail before destination effects.
- **`func init`:** all existing Stack options, adoption/repair, safe occupied non-project directories, write-if-missing behavior, complete-project refusal, force confirmation, `.git` protection, and required configuration failures.
- **`func new`:** project resolution, existing command grammar, template listing versus invocation, item collisions, guarded modifications, and no unrelated initialization configuration writes. Verify that a confirmed uninitialized target receives actionable `func init` guidance, while ambiguity, inspection/access failures, corrupt or malformed configuration, unsupported projects, and incompatibility retain their corresponding diagnostics. An undetermined resolution failure must not imply initialization is required. Resolution failures stop before discovery; incompatibility stops before generation. None of these failures may initialize a project or modify the destination.
- **Publication:** path/ancestor/case checks, guarded no-replace or replacement on supported operating systems, cancellation, cleanup, and truthful partial-effect reporting.
- **Packaging:** isolated test registration versus real workload registration; missing or incompatible installed content; retry and version coordination.

## Validation status and limitations

An independent review of the deferred fixture prototype passed with a nonblocking finding; it was inspection-only and did not review the foundation. The historical 108-test run above applies to the foundation commit, not later review changes. **RUNTIME VALIDATION REQUIRED** for production generation and host qualification.

Separate Windows fixture qualification does not qualify this candidate. Focused unit tests do not establish installed-hive snapshot integration, general source integrity, guarded physical publication, cross-platform behavior, Functions Host discovery, or supported Stack coverage; these remain unqualified.

Keep current initializers and the existing Quickstart route until their replacements or migrations are implemented and qualified. Package install/update/uninstall/search, producer-pipeline expansion, a general task framework, and physical engine extraction are outside PR 1.

Earlier investigations are preserved in the [archive](archive/). They do not replace current source, accepted API decisions, or executed qualification evidence.
