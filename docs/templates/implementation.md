# Implementing the Template Engine

**Status:** Proposed implementation plan

This document describes the existing CLI v5 infrastructure, what remains to be implemented, and how to validate the first `func init` and `func new` integration. The [architecture](architecture.md) defines the required behavior and safety boundaries.

## What exists today

The repository baseline described by the current design includes:

- `TemplaterFactory`, `TemplateEngineContext`, `TemplateEngineSession`, and `Templater`: creation/disposal, a dedicated CLI template hive, installed-template listing, and metadata projection. The new command-facing resolution and invocation behavior is not complete.
- `TemplateCatalogEntry` and parameter projection: template identity and display metadata. Projection alone does not evaluate conditional requirements or defaults.
- Template constraint readers and evaluators: reusable helpers that are not yet wired into the complete command path.
- `InitCommand`, `IProjectInitializer`, and Stack metadata: six existing project initializers, Stack-specific options, adoption, and configuration repair. They remain operative until replacements are qualified.

- `NewCommand` retains the current public argument surface. Its migration handler reports scaffolding and template listing unavailable and returns exit code 1 before modifying the target. The first-run coordinator skips `func new`, so the unavailable-command handler returns its error without running setup.
- `FirstRunCoordinator`: for eligible commands, setup can complete before the handler and ask the user to rerun. `func new` skips first-run setup.
- `QuickstartCommand`: a separate existing manifest/provider/scaffolding route, not the new Template Engine implementation.
- Test-only local template registration in isolated hives. Workload deployment does not yet establish the required production registration into the CLI template hive.

### Existing initialization limitations

The current `func init` path has known safety limitations: it can create the destination before validation, perform force cleanup before all Stack-specific checks, remove `.git` pointer files, and report success after some required configuration writes fail.

These behaviors belong to the existing implementation. The proposed template-based path must correct them before replacing an initializer.

Useful source entry points include `src/Func/Templates/Engine/`, `src/Func/Commands/InitCommand.cs`, `src/Func/Commands/NewCommand.cs`, `src/Abstractions/Projects/`, and `src/Workloads/Stacks/`. Recheck the exact implementation branch before treating any type as available: the legacy cleanup and ongoing feature work may have changed the baseline.

## Initial implementation

Build the smallest working command-facing model first, then fill in behavior incrementally.

1. **Establish the API surface.** Create a `Templater` from the factory and command context. Support listing or resolving installed templates and invoking an exact selected template. Keep the engine session internal. Use the proposed group/catalog/resolved-template concepts where they make the API clearer; do not create wrappers without a consumer.

2. **Qualify the engine seam.** With representative project and parameterized function templates installed in an isolated hive, prove metadata discovery, Stack filtering, exact selection, parameter evaluation, and invocation against selected content. Use multiple metadata variants to expose filtering and ambiguity behavior.

3. **Integrate the CLI commands.** Preserve `func init` adoption/repair and current Stack behavior. Add one template-based project route and one `func new` item route for the same Stack. Keep unmigrated initializers working.

4. **Add guarded publication.** Render privately, validate target effects, preserve non-force behavior, protect Git metadata, persist required configuration correctly, and report failures and partial effects.

5. **Connect production content.** Once template content owners and workload owners confirm package ownership and layout, register installed workload templates into the CLI hive. Do not make discovery install packages.

6. **Qualify the release surface.** Run command regressions, actual Functions Host discovery, installed-platform tests, and the approved supported-Stack inventory before enabling public paths.

Steps 3 and 4 form one safe command increment: no integrated route may change a destination before the guarded publication requirements are in place.

This order is a delivery plan, not a mandate for a large framework or a complete catalog before the first engine proof.

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

## Dependencies and open decisions

| Area | Needed before |
| --- | --- |
| Command-facing API, group/variant and alias behavior | Freezing signatures and public selection behavior |
| Microsoft.TemplateEngine parameter and selected-content APIs | Claiming supported evaluation and exact invocation |
| Representative real project and item content | Completing the first useful end-to-end proof |
| Workload package layout and production registration | Making templates available through installed workloads |
| Supported Stack scope, platform policy, and force behavior | Public activation and release acceptance |

Engine work with isolated representative content does not need to wait for the entire production catalog. A public template-backed journey does need approved content and production registration.

## Tests and acceptance

### First working journey

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
- **`func new`:** project resolution, existing command grammar, template listing versus invocation, item collisions, guarded modifications, and no unrelated initialization configuration writes. Verify that invoking `func new` outside an initialized Functions project fails with actionable guidance to run `func init`, without initializing a project or modifying the destination.
- **Publication:** path/ancestor/case checks, guarded no-replace or replacement on supported operating systems, cancellation, cleanup, and truthful partial-effect reporting.
- **Packaging:** isolated test registration versus real workload registration; missing or incompatible installed content; retry and version coordination.

## Qualification and limitations

Static source and tests do not prove the published engine works end to end. Runtime qualification is still required for Microsoft.TemplateEngine parameter and mount behavior, selected-content integrity, actual Host discovery, filesystem operations across supported OS/RIDs, package acquisition/registration, and supported Stack coverage.

Keep current initializers and the separate Quickstart route until their replacement or migration is explicitly in scope and validated. Package install/update/uninstall/search, producer pipeline expansion, template-first or multi-project Quickstart UX, general template actions, and physical engine extraction are outside this initial increment.

Earlier investigations are preserved in the [archive](archive/). They do not replace current source, accepted API decisions, or executed qualification evidence.
