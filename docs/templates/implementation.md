# Implementing the Template Engine

**Status:** Template-driven project initialization in `func init`; not release-qualified

This document describes the existing CLI v5 infrastructure, what remains to be implemented, and how to validate the first `func init` and `func new` integration. The [architecture](architecture.md) describes the proposed behavior and safety boundaries.

## What exists today

The current CLI includes:

- `TemplaterFactory`, `TemplateEngineContext`, `FuncTemplateEngineHost`, and `Templater`: a command-scoped engine environment, a persistent CLI template hive, installed-template discovery through `TemplatePackageManager.GetTemplatesAsync`, filtered project-template discovery, and native dry-run validation and instantiation through `FuncTemplateInfo.InstantiateAsync`, which delegates to `TemplateCreator`. `Templater` does not require a single project template; several can coexist. The factory gets engine components from `IFuncTemplateComponentProvider.Get()`, which combines built-in components, `ICoreTemplateComponent` registrations, and supported bridges for workload `ITemplateComponent` contracts; no separate engine-session abstraction is used.
- `FuncTemplateInfo` and `TemplateResolutionResult`: discovery returns wrappers that forward `ITemplateInfo` metadata, retain the original engine template as `Template`, and expose parsed `func.host.json` metadata through `HostData`. Filtered project-template discovery also retains each candidate's native `ITemplateMatchInfo` in `TemplateResolutionResult.Matches`, keyed by the candidate; the caller still selects among all candidates.
- `IProjectInitializer` and Stack metadata: six existing project initializers and their Stack-specific options. `InitializeAsync` remains the existing Stack scaffolding path until replacements are qualified. `ConfigureProjectAsync` is the Stack-owned, non-scaffolding setup hook for template-created projects; it defaults to no operation and the .NET initializer does not override it.
- `func init` uses a template when project candidates are found for the resolved language and exactly one has the `func-{stack}` short name. `TemplateLanguage.ToTemplateTag` maps the stack's canonical language ID to the template language tag (for example `csharp` to `C#`); `.func/config.json` keeps the canonical ID. Public parsing is route-neutral for Stack-contributed options: they are shown in help, but their typed validation does not run before routing. The template route binds every alias the selected template authors in `func.host.json` according to its native parameter definitions, including spellings a Stack also registers with a different type or arity, ignores stale host symbols, and leaves omitted values to Template Engine defaults. Stack-only aliases the template does not author are rejected, and a native parameter assigned more than once is rejected. With no candidates, the existing Stack route parses the same tokens against the Stack's typed options; when candidates exist without a unique default, it fails.
- `src/Func/Commands/Init/`: `InitCommand` (a `TemplateCommand`) parses the request and calls `InitWorkflow.ResolveAsync`, which validates the stack and language against the installed stack descriptors before any route writes. An unknown stack, a multi-language stack without a language, an unsupported language, an ambiguous or conflicting worker runtime, and an undetectable language on adoption fail without changes. `InitCommand` then routes the resolved initializer and language. `TemplatedInitHandler` owns template creation: `func-{stack}` default selection, host-alias binding, unknown template-option rejection, Template Engine dry-run preflight, destination preparation for `--force`, the `.func/config.json` write, creation, and then `IProjectInitializer.ConfigureProjectAsync`. It checks for cancellation after Stack setup, so a cancelled setup never reports success; created files remain, and it never calls `InitializeAsync`. Template post-actions aren't run yet; #6023 tracks that. `TemplatedInitHandler` also owns adoption and configuration repair of existing projects, which create no project content and therefore reject template and Stack options, including registered Stack options, before prompting or writing. `LegacyInitCommand` keeps the existing Stack scaffolding path through `InitializeAsync`, after typed validation of the Stack options, and writes the config before it. Every route reads and writes `.func/config.json` only through `IProjectStackConfiguration`. If the config can't be saved, `func init` exits 1 before any project file is created. If creation or Stack setup fails, the config stays and the folder may hold a partly created project: `func init` exits 1, names what failed, and says to start over with `--force`, which deletes everything in the folder except `.git`; without `--force`, the same command stops because the folder already holds a project. `InitWorkflow.PrepareDestinationAsync` creates the folder and, with `--force`, clears it; if either step fails, `func init` exits 1 without writing a new config and says to run the same command again. Only file system errors and `GracefulException` from Stack creation or setup are reported as a failed creation or setup. Exceptions that escape CLI-owned operations reach the CLI's unexpected-error handling; Template Engine generation failures come back as a failed result. #6022 tracks keeping the old content until the new project is created. `InitWorkflow` holds the validation and helpers shared by both flows.
- `TemplateCommand` and `TemplateArgumentBinder`: template commands disable treating unmatched tokens as errors; the binder receives the leftover tokens, binds recognized template options, and rejects the rest. Diagnostics come from System.CommandLine parse results, including invalid Boolean values such as `--no-http=garbage`. `PathArgument` never consumes a `-`-prefixed token, so other commands still report such tokens as unrecognized. The positional `[path]` is transitional: `func init <path> [template options]`. The path must precede template and Stack options, and a path that follows any of them is rejected.
- `NewCommand` retains the current public argument surface. Its migration handler reports scaffolding and template listing unavailable and returns exit code 1 before modifying the target. The first-run coordinator skips `func new`, so the unavailable-command handler returns its error without running setup.
- `FirstRunCoordinator`: for eligible commands, setup can complete before the handler and ask the user to rerun. `func new` skips first-run setup.
- Workload template roots: a workload can declare an optional `templateRoot` in `workload.json`. The workload store persists it and hydrates the package root, the loader resolves it for active runtime workloads, and `WorkloadTemplatePackageProviderFactory` exposes the roots to the engine as read-only template packages. The .NET, Go, Node.js, and Python workloads package their project, HTTP trigger, and Timer trigger templates under `content/templates` and declare that directory as `templateRoot`. The .NET HTTP trigger template declares an `AuthorizationLevel` parameter (`Anonymous`, `Function`, or `Admin`; default `Function`) with `--auth-level` and `-a` host aliases. Installs that predate `templateRoot` persistence need a reinstall.
- Test-only local template installation in isolated hives.
- Real-engine discovery tests cover persistent cache reuse, source addition/removal, missing roots, active-version replacement, same-version timestamp refresh, native duplicate precedence, and aggregation with a managed provider. The cache persists across commands; the workload source list is cached for each engine instance.

The discovery foundation is integrated with `func init` only. `func new`, Functions-specific constraints, selected-content integrity checks, and private rendering remain out of scope, and post-action execution remains out of scope for `func init`; the #5963 dispatcher is merged but not yet wired (#6023). The previous session, catalog projection, and custom constraint helpers are not part of the current foundation.

The Python HTTP trigger template declares `--auth-level` and `-a` host aliases for its `AuthorizationLevel` parameter; these require command integration before they can be used in `func new`.

Microsoft.TemplateEngine accepts an explicitly empty choice value even when it is not a declared choice. For the .NET HTTP trigger template this produces an incomplete `AuthorizationLevel` member, so command integration must reject empty explicit choices before invocation.

### Existing initialization limitations

The existing Stack initializer path retains its safety limitations: after the shared stack and language validation, it can create the destination before Stack-specific checks and perform force cleanup before them. The template path validates with Template Engine dry-run before force confirmation, cleanup, generation, or configuration writes, and refuses planned overwrites unless `--force` is used. Both paths share the `--force` cleanup, which preserves a `.git` directory but deletes a `.git` pointer file, so `--force` is not yet safe for Git worktrees.

These behaviors belong to the existing implementation. The proposed template-based path must correct them before replacing an initializer.

Useful source entry points include `src/Func/Templates/Engine/`, `src/Func/Commands/Init/`, `src/Func/Commands/Templating/`, `src/Func/Commands/NewCommand.cs`, `src/Abstractions/Projects/`, and `src/Workloads/Stacks/`. Recheck the exact implementation branch before treating any type as available: the legacy cleanup and ongoing feature work may have changed the baseline.

## Next implementation steps

`func init` provides one template-driven project initialization route. Remaining work should stay separate from this narrow integration.

The next command slice is .NET 10 isolated/C# using the workload-provided, constraint-free project, HTTP trigger, and Timer trigger templates. Other Stacks, constraints, advanced post-actions, standalone package commands, and remote search are separate increments.

1. **Qualify the init path.** Exercise actual supported Stack templates and verify parameters, generated project resolution, force behavior, cancellation, and `.git` preservation, including Git worktree pointer files, on supported platforms. One CLI test runs the real .NET Stack with the shipped `func-dotnet` template; the remaining routing tests use local fixture templates.

2. **Connect production content.** The .NET, Go, Node.js, and Python workloads already package their templates and emit `templateRoot`; other Stack workloads follow the same pattern. These core templates are versioned and serviced with the workload, so changing one requires a new workload version; see [template versioning](architecture.md#core-and-independently-packaged-template-versioning). The existing store, loader, and provider expose them to the engine without a separate managed-package installation. Verify the actual packaged install/update/uninstall path and advance timestamps on same-version reinstalls. Keep the missing-root diagnostic as explicit follow-up. Discovery must not install packages or force a cache rebuild on every command.

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
| Approved workload content and `templateRoot` packaging | Making templates available through installed workloads |
| Supported Stack scope, platform policy, and force behavior | Public activation and release acceptance |

Engine work with isolated representative content does not need to wait for the entire production catalog. A public template-backed journey does need approved content and production packaging.

## Tests and acceptance

These are acceptance criteria for release qualification, not a claim that the complete end-to-end journey has completed.

### First working journey

Use a real project template and a parameterized HTTP-function template for one supported Stack:

1. Stage both under a workload `templateRoot` and expose it through the workload provider using an isolated CLI hive. This fixture is not proof of production packaging.
2. Run `func init` and verify the resulting project with the normal project resolver.
3. Run `func new` against that project, passing a required parameter.
4. Verify the generated function uses the supplied parameter.
5. Start the approved Functions Host and verify actual function discovery and invocation.

A `host.json` file, successful listing, or basic text-file fixture alone does not establish this proof. One representative Stack does not establish GA coverage.

### Focused correctness cases

- **Discovery and selection:** missing content, native duplicate-identity precedence, and ambiguous aliases between distinct identities. Constraint eligibility and richer variant selection are separate follow-ups; exact selection must not silently substitute another template.
- **Parameters:** candidate-specific options before final selection; conditional required/disabled/default values; explicit empty and repeated values; interactive and noninteractive errors.
- **Content integrity:** unsupported engine features fail before destination effects; missing or unreadable selected content fails without substituting another template.
- **`func init`:** all existing Stack options, adoption/repair, safe occupied non-project directories, write-if-missing behavior, complete-project refusal, force confirmation, `.git` protection, and required configuration failures.
- **`func new`:** project resolution, existing command grammar, template listing versus invocation, item collisions, guarded modifications, and no unrelated initialization configuration writes. Verify that invoking `func new` outside an initialized Functions project fails with actionable guidance to run `func init`, without initializing a project or modifying the destination.
- **Publication:** path/ancestor/case checks, guarded no-replace or replacement on supported operating systems, cancellation, cleanup, and truthful partial-effect reporting.
- **Packaging and cache:** real-engine isolated-hive tests cover added/removed roots, missing directories, active-version replacement, and strictly newer timestamps. Still qualify the actual workload package/install path, multi-owner retention, and supported OS/RIDs. Cache refresh is lazy on discovery, not an eager installer action.

## Qualification and limitations

Static source and tests do not prove the published engine works end to end. Runtime qualification is still required for Microsoft.TemplateEngine parameter and mount behavior, actual Host discovery, filesystem operations across supported OS/RIDs, workload packaging, and supported Stack coverage.

Keep current initializers until their replacement or migration is explicitly in scope and validated. Future `func templates` lifecycle commands use the engine's managed provider in the same hive; known-package-ID installation does not require the remote search index. No such commands, producer pipeline expansion, template-first or multi-project Quickstart UX, general template actions, or physical engine extraction are included in this increment.

Earlier investigations are preserved in the [archive](archive/). They do not replace current source, accepted API decisions, or executed qualification evidence.
