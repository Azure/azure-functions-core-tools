## 1. Template Integration Prerequisites

- [ ] 1.1 Add a context-free template metadata catalog backed by the shared func hive without evaluating command constraints or producing invocable templates.
- [ ] 1.2 Add project-type listing and reference resolution that exposes identity, aliases, group identity, language, precedence, visibility, and package origin.
- [ ] 1.3 Validate required project-template language tags and retain diagnostics for missing or unsupported language metadata.
- [ ] 1.4 Ensure package install, update, and uninstall invalidate context-free catalog snapshots.
- [ ] 1.5 Extend project-type `ResolvedTemplate` invocation with a CLI configuration step while leaving item-type invocation unchanged.
- [ ] 1.6 Implement `.func/config.json` generation using either an embedded companion template or equivalent direct generation with synthesized effects.
- [ ] 1.7 Add composite creation-effects adaptation for project and CLI configuration file changes while keeping TemplateEngine types internal.
- [ ] 1.8 Preflight project and configuration effects together and reject project templates that target the reserved `.func/config.json` path.
- [ ] 1.9 Preserve project-template primary outputs and post-actions in the composite invocation result.
- [ ] 1.10 Add focused integration tests for project dry-run, create, overlap rejection, configuration failure, and unchanged item-template behavior.

## 2. Project Stack Contract

- [ ] 2.1 Replace public `IProjectInitializer` with metadata-only `IProjectStack` in the abstractions project.
- [ ] 2.2 Define canonical stack ID, display name, worker runtime aliases, supported languages, and language aliases on `IProjectStack`.
- [ ] 2.3 Implement `InstalledProjectStackCatalog` with duplicate-stack validation and case-insensitive canonical and alias lookup.
- [ ] 2.4 Preserve a one-to-many canonical-language-to-stack mapping instead of first-registration-wins behavior.
- [ ] 2.5 Migrate .NET workload registration and stack metadata to `IProjectStack`.
- [ ] 2.6 Migrate Node workload registration and stack metadata to `IProjectStack`.
- [ ] 2.7 Migrate Python workload registration and stack metadata to `IProjectStack`.
- [ ] 2.8 Migrate Go workload registration and stack metadata to `IProjectStack`.
- [ ] 2.9 Migrate PowerShell workload registration and stack metadata to `IProjectStack`.
- [ ] 2.10 Update workload loading tests to assert the new stack contract and remove initializer behavior expectations.

## 3. Project Template Packages

- [ ] 3.1 Add project-template package structure and delivery wiring compatible with the func TemplateEngine hive.
- [ ] 3.2 Create .NET C# and F# project-template variants with `type=project`, required language tags, target-framework symbols, and func host metadata.
- [ ] 3.3 Create Node JavaScript and TypeScript project-template variants with required language tags, bundle symbols, package-restore controls, and post-actions.
- [ ] 3.4 Create Python project-template variants with required language tags and bundle symbols.
- [ ] 3.5 Create Go project-template variants with required language tags, bundle symbols, module settings, and tidy post-actions.
- [ ] 3.6 Create PowerShell project-template variants with required language tags and applicable project symbols.
- [ ] 3.7 Move common bundle channel and no-bundle options from workload registrations into project-template symbols.
- [ ] 3.8 Add `func.host.json` aliases, descriptions, choices, required status, and visibility for every migrated project-template parameter.
- [ ] 3.9 Ensure no project template contains or generates `.func/config.json`.
- [ ] 3.10 Add template authoring and instantiation tests for every canonical stack-language combination.
- [ ] 3.11 Ensure setup or package bootstrap flows make default project templates available for each installed stack.

## 4. Init Command Surface

- [ ] 4.1 Introduce immutable `InitExecutionRequest` carrying the target path, static filters, invocation flags, output mode, and raw template tokens.
- [ ] 4.2 Add `--template` / `-t`, `--non-interactive`, and `--dry-run` to `InitCommand` while retaining positional `[path]`, stack, language, name, force, and output options.
- [ ] 4.3 Remove workload-contributed option registration from the command constructor and help output.
- [ ] 4.4 Preserve unmatched template tokens only for strict Stage B parsing and prevent unmatched tokens from reaching a successful path directly.
- [ ] 4.5 Reduce `InitCommand.ExecuteAsync` to static binding, request construction, and one orchestration call.
- [ ] 4.6 Update init-specific reserved aliases supplied to the shared template parser.

## 5. Initialization State and Adoption

- [ ] 5.1 Extract project-state detection into a testable service or focused runner component without changing empty, adoptable, healable, and initialized classifications.
- [ ] 5.2 Route adoptable projects through installed stack metadata and CLI configuration generation without loading project templates.
- [ ] 5.3 Route partial language healing through canonical stack-language resolution and atomic configuration merge without loading project templates.
- [ ] 5.4 Reject `--template` on adoption or healing unless `--force` selects reinitialization.
- [ ] 5.5 Preserve refusal for fully initialized projects without force.
- [ ] 5.6 Persist canonical language during adoption and healing, including single-language stacks.
- [ ] 5.7 Replace best-effort configuration warnings with explicit success, user failure, or partial-state outcomes as appropriate.

## 6. Compatibility Matrix

- [ ] 6.1 Define immutable project-template metadata and `InitCandidate` models for stack, canonical language, and template group reference.
- [ ] 6.2 Implement `InitCandidateResolver` by intersecting installed stack languages with project-template variant language tags.
- [ ] 6.3 Preserve all stack owners when multiple stacks support one canonical language.
- [ ] 6.4 Exclude stack languages for which no project-template variant is installed.
- [ ] 6.5 Implement case-insensitive stack and language filters using canonical names and declared aliases.
- [ ] 6.6 Implement project-template identity and short-name filtering with project-type enforcement and wrong-type diagnostics.
- [ ] 6.7 Produce targeted outcomes for unknown filters, stack-language conflicts, template-stack conflicts, and missing applicable templates.
- [ ] 6.8 Add immutable-filter tests covering every stack-first, language-first, template-first, and fully explicit combination.

## 7. Selection and Prompting

- [ ] 7.1 Apply every explicit stack, language, and template filter before automatic selection or prompting.
- [ ] 7.2 Auto-select any unresolved dimension with exactly one remaining canonical value.
- [ ] 7.3 Prompt stack, then language, then template when no template filter was supplied and choices remain.
- [ ] 7.4 Prompt only compatible stack and language choices when a project template was supplied first.
- [ ] 7.5 Avoid special-casing the basic template name and auto-select only when one applicable group remains.
- [ ] 7.6 Fail non-interactively with canonical choices and corrective option guidance whenever a stack, language, or template prompt would be required.
- [ ] 7.7 Preserve cancellation through every selection prompt and return no partially selected mutable state.

## 8. Prospective Context and Parsing

- [ ] 8.1 Create prospective template project context from target directory, selected canonical stack, and selected canonical language.
- [ ] 8.2 Leave bundle ID and version unavailable during project initialization and test bundle-dependent constraints fail closed.
- [ ] 8.3 Create one command-scoped `Templater` after stack and language selection and reuse it through authoritative resolution and invocation.
- [ ] 8.4 Resolve the selected project-template reference again through the context-bound `Templater` and surface catalog-to-execution drift.
- [ ] 8.5 Reuse the `func new` candidate parser and alias coordinator for project-template symbols.
- [ ] 8.6 Parse raw template tokens independently for every remaining variant and distinguish invalid explicit input from unresolved required input.
- [ ] 8.7 Filter argument-compatible identities before applying highest remaining precedence.
- [ ] 8.8 Prompt only unresolved visible required symbols and fail non-interactively with every missing effective alias.
- [ ] 8.9 Perform the final canonical reparse after prompted values and pass only canonical symbol mappings to invocation.

## 9. Force, Dry-Run, and Invocation

- [ ] 9.1 Plan deletion of all target content except `.git` for forced reinitialization.
- [ ] 9.2 Confirm destructive cleanup interactively and treat non-interactive `--force` as explicit authorization.
- [ ] 9.3 Complete stack, language, template, parsing, and combined effect preflight before deleting target content.
- [ ] 9.4 Invoke the selected project `ResolvedTemplate` with target path, canonical symbols, name, conflict policy, and create or dry-run mode.
- [ ] 9.5 Combine forced cleanup, project-template, CLI configuration, and post-action effects in deterministic execution order.
- [ ] 9.6 Reconcile dry-run changes following planned cleanup so deletion and recreation are represented accurately.
- [ ] 9.7 Ensure dry-run creates no directories, writes no project or configuration files, and executes no post-actions.
- [ ] 9.8 Run project-template post-actions by default only after project and CLI configuration generation succeed.
- [ ] 9.9 Report partial initialization without deleting generated project files if configuration generation fails after project creation.
- [ ] 9.10 Dispose the command-scoped `Templater` and propagate cancellation through preflight, creation, configuration, and post-actions.

## 10. Rendering and Outcomes

- [ ] 10.1 Add func-owned init outcomes for no stacks, duplicate stacks, incompatibility, wrong template type, missing packages, ambiguity, invalid arguments, and restricted templates.
- [ ] 10.2 Render stack, language, and template prompts using canonical values and user-facing display labels.
- [ ] 10.3 Render no-template guidance with the selected stack and language and a `func new install` next action.
- [ ] 10.4 Render ordered plain dry-run effects for cleanup, project files, `.func/config.json`, and post-actions.
- [ ] 10.5 Render the same dry-run and creation data through stable JSON output.
- [ ] 10.6 Render declined cleanup, successful adoption, successful healing, successful creation, and partial initialization distinctly.
- [ ] 10.7 Wrap only documented user failures at the command boundary and allow unexpected defects to retain stack traces.

## 11. Legacy Removal

- [ ] 11.1 Remove `InitContext`, `IInitOptionRegistry`, `InitOptionRegistry`, and common workload init option factories after template migration.
- [ ] 11.2 Remove .NET workload initializer file-generation and nested `dotnet new` execution code.
- [ ] 11.3 Remove Node workload project-file generation and package-install execution code migrated to templates and post-actions.
- [ ] 11.4 Remove Python workload project-file generation code migrated to templates.
- [ ] 11.5 Remove Go workload project-file generation and tidy execution code migrated to templates and post-actions.
- [ ] 11.6 Remove PowerShell workload initializer scaffolding code migrated to templates.
- [ ] 11.7 Remove initializer dependencies from `func new`, template option hydration, and language group resolution in favor of `IProjectStack` or template-owned metadata.
- [ ] 11.8 Remove legacy initializer fallback from `InitCommand` and dependency injection registration.

## 12. Unit and Integration Tests

- [ ] 12.1 Test static init parsing for path, stack, language, template, force, non-interactive, dry-run, and raw template tokens.
- [ ] 12.2 Test duplicate stack IDs, aliases, one-to-many language ownership, and no-installed-stack guidance.
- [ ] 12.3 Test project-template type and language metadata validation, including item-type and untagged-template diagnostics.
- [ ] 12.4 Test no-filter, stack-first, language-first, template-first, and fully explicit selection flows.
- [ ] 12.5 Test auto-selection, interactive prompting order, and every non-interactive ambiguity diagnostic.
- [ ] 12.6 Test shared template parsing, alias collisions, invalid input, missing required values, canonical mappings, and precedence timing.
- [ ] 12.7 Test prospective stack and language host bindings and unavailable bundle defaults.
- [ ] 12.8 Test empty, initialized, adoptable, healable, forced, and declined-force state paths.
- [ ] 12.9 Test `.func/config.json` always contains canonical stack and language and is never owned by project templates.
- [ ] 12.10 Test combined project and configuration effects for dry-run and actual invocation.
- [ ] 12.11 Test forced dry-run cleanup ordering, `.git` preservation, overlap rejection, and unchanged filesystem.
- [ ] 12.12 Test default post-action execution, dry-run suppression, configuration-failure suppression, and cancellation.
- [ ] 12.13 Test missing applicable templates never fall back to workload scaffolding.
- [ ] 12.14 Add end-to-end initialization coverage for every in-repository stack-language project template.

## 13. Documentation and Validation

- [ ] 13.1 Update `func init --help` for project-template selection, non-interactive behavior, dry-run, and template-specific options.
- [ ] 13.2 Document installed stack and project-template requirements, language-tag authoring, and `func new install` guidance.
- [ ] 13.3 Document `.func/config.json` ownership, mandatory canonical stack and language, and reserved template output path.
- [ ] 13.4 Document destructive force behavior and ordered dry-run effects.
- [ ] 13.5 Document the breaking `IProjectInitializer` to `IProjectStack` workload migration.
- [ ] 13.6 Run targeted abstraction, workload, init command, template integration, parser, renderer, and project-template tests.
- [ ] 13.7 Run restore, the clean Release build with warnings treated as errors, and the full test suite.
