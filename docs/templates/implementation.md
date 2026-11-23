# Implementing the Template Engine

**Status:** Local `func init` proof of concept; not release-qualified

This document describes the existing CLI v5 infrastructure, what remains to be implemented, and how to validate the first `func init` and `func new` integration. The [architecture](architecture.md) describes the proposed behavior and safety boundaries.

## What exists today

The current CLI includes:

- `TemplaterFactory`, `TemplateEngineContext`, `FuncTemplateEngineHost`, and `Templater`: a command-scoped engine environment, an isolated CLI template hive, filtered project-template discovery, native dry-run validation, and instantiation through `TemplatePackageManager` and `TemplateCreator`. The factory passes DI-registered `ITemplateComponent` instances directly to the host; no separate engine-session abstraction is used.
- `FuncTemplateInfo` and `TemplateResolutionResult`: discovery returns wrappers that forward `ITemplateInfo` metadata and retain the original engine template. `HostConfigPlace` is available, but its file is not read yet.
- `InitCommand`, `IProjectInitializer`, and Stack metadata: six existing project initializers, Stack-specific options, adoption, and configuration repair. They remain operative until replacements are qualified.
- `func init` uses a template when project candidates are found for the resolved language and exactly one has the `func-{stack}` short name. It binds the selected template's host aliases to native parameter definitions, ignores stale host symbols, and leaves omitted values to Template Engine defaults. With no candidates, it falls back to the existing Stack initializer; when candidates exist without a unique default, it fails.
- `TemplateProjectInitializer`: owns template-driven project initialization after `InitCommand` finds applicable candidates, including `func-{stack}` default selection, host-alias binding, unknown template-option rejection, Template Engine dry-run preflight, and creation through `Templater`. It never calls `IProjectInitializer`. `InitCommand` keeps routing, adoption and repair, the existing Stack initializer path, destination preparation for `--force`, and the `.func/config.json` write. Option registration remains on the public command for System.CommandLine parsing and help; semantic interpretation and execution are separated by flow.
- `NewCommand` retains the current public argument surface. Its migration handler reports scaffolding and template listing unavailable and returns exit code 1 before modifying the target. The first-run coordinator skips `func new`, so the unavailable-command handler returns its error without running setup.
- `FirstRunCoordinator`: for eligible commands, setup can complete before the handler and ask the user to rerun. `func new` skips first-run setup.
- `QuickstartCommand`: a separate existing manifest/provider/scaffolding route, not the new Template Engine implementation.
- Test-only local template registration in isolated hives. Workload installation does not yet register its template folders in the CLI template hive.

This proof of concept uses installed content already present in the CLI hive; workload installation does not yet register production template folders. `func new`, general template selection, selected-content integrity checks, private rendering, and approved post-action behavior remain out of scope. The previous session, catalog projection, and custom constraint helpers are not part of the current foundation.

### Existing initialization limitations

The existing Stack initializer path retains its safety limitations: it can create the destination before all validation, perform force cleanup before all Stack-specific checks, remove `.git` pointer files, and report success after some required configuration writes fail. The template path validates with Template Engine dry-run before force confirmation, cleanup, generation, or configuration writes, and refuses planned overwrites unless `--force` is used. Both paths share the `.func/config.json` write, which currently warns and completes when that write fails; returning failure as the [architecture](architecture.md) requires is a follow-up.

These behaviors belong to the existing implementation. The proposed template-based path must correct them before replacing an initializer.

Useful source entry points include `src/Func/Templates/Engine/`, `src/Func/Commands/InitCommand.cs`, `src/Func/Commands/TemplateProjectInitializer.cs`, `src/Func/Commands/NewCommand.cs`, `src/Abstractions/Projects/`, and `src/Workloads/Stacks/`. Recheck the exact implementation branch before treating any type as available: the legacy cleanup and ongoing feature work may have changed the baseline.

## Next implementation steps

The local proof of concept exposes one template-backed `func init` route. Remaining work should stay separate from this narrow integration.

1. **Qualify the init path.** Exercise actual supported Stack templates and verify parameters, generated project resolution, force behavior, cancellation, and `.git` preservation on supported platforms. The current tests use local fixture templates.

2. **Connect production content.** Register workload-owned template folders in the CLI hive on install and remove or replace them on uninstall/update. Verify cache invalidation; discovery must not install packages or force a full cache rebuild on each command.

3. **Implement `func new` separately.** Preserve its existing public command surface, resolve the initialized project context, and validate item collisions before publication. Keep the Quickstart route separate.

4. **Strengthen publication safety.** The current init route relies on dry-run effects before generation and retains the existing directory-clearing policy for `--force`. A broader private-render and guarded-publication flow needs explicit review and cross-platform validation before release.

5. **Qualify the release surface.** Run command regressions, actual Functions Host discovery, installed-platform tests, and the approved supported-Stack inventory before enabling public paths.

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
| Workload template folder layout and install-time registration | Making templates available through installed workloads |
| Supported Stack scope, platform policy, and force behavior | Public activation and release acceptance |

Engine work with isolated representative content does not need to wait for the entire production catalog. A public template-backed journey does need approved content and production registration.

## Tests and acceptance

These are acceptance criteria for release qualification, not a claim that the complete end-to-end journey has completed.

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
- **Packaging:** isolated test registration versus real workload registration; registration on install, removal on uninstall, and replacement on update; repeated registration of the same version; missing or unreadable registered folders; cache refresh after a folder changes.

## Qualification and limitations

Static source and tests do not prove the published engine works end to end. Runtime qualification is still required for Microsoft.TemplateEngine parameter and mount behavior, selected-content integrity, actual Host discovery, filesystem operations across supported OS/RIDs, package acquisition/registration, and supported Stack coverage.

Keep current initializers and the separate Quickstart route until their replacement or migration is explicitly in scope and validated. Package install/update/uninstall/search, producer pipeline expansion, template-first or multi-project Quickstart UX, general template actions, and physical engine extraction are outside this initial increment.

Earlier investigations are preserved in the [archive](archive/). They do not replace current source, accepted API decisions, or executed qualification evidence.
