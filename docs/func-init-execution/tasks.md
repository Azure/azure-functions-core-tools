## 1. Template Integration Prerequisites

- [ ] 1.1 Use the project catalog entries, eligibility, and declared projects defined by `template-engine-integration` and `func-init-quickstarts` instead of a separate init catalog.
- [ ] 1.2 Add project-type listing and reference resolution that exposes identity, aliases, group identity, language, precedence, visibility, and package origin.
- [ ] 1.3 Treat project-template language tags as optional variant metadata, with configuration actions authoritative for each project's stack and language.
- [ ] 1.4 Evaluate project-template constraints when listing in the init context, and enforce that result through selection and invocation.
- [ ] 1.5 Consume the trusted Functions project configuration action contract and project its metadata without exposing TemplateEngine action types to commands.
- [ ] 1.6 Resolve active configuration actions against final primary-output paths after conditions and file renames.
- [ ] 1.7 Add planned CLI-owned `.func/config.json` effects for project configuration actions while leaving item-template effects unchanged.
- [ ] 1.8 Preflight action identity, mandatory failure semantics, canonical stack/language supported by an installed stack, primary-output references, target containment, unique project roots, and output collisions.
- [ ] 1.9 Preserve resolved primary outputs and separate mandatory configuration actions from ordinary post-actions in invocation results.
- [ ] 1.10 Add focused integration tests for action projection, renamed primary outputs, dry-run, creation, overlap rejection, configuration failure, and unchanged item-template behavior.

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
- [ ] 2.10 Migrate Java workload registration and stack metadata to `IProjectStack`.
- [ ] 2.11 Update workload loading tests to assert the new stack contract and remove initializer behavior expectations.

## 3. Project Template Packages

- [ ] 3.1 Add project-template package structure and delivery wiring compatible with the func TemplateEngine hive.
- [ ] 3.2 Create .NET C# and F# project-template variants with `type=project`, language tags, target-framework symbols, and func host metadata.
- [ ] 3.3 Create Node JavaScript and TypeScript project-template variants with language tags, bundle symbols, package-restore controls, and post-actions.
- [ ] 3.4 Create Python project-template variants with language tags and bundle symbols.
- [ ] 3.5 Create Go project-template variants with language tags, bundle symbols, module settings, and tidy post-actions.
- [ ] 3.6 Create PowerShell project-template variants with language tags and applicable project symbols.
- [ ] 3.7 Create Java project-template variants with language tags, Maven project files, and bundle symbols.
- [ ] 3.8 Move common bundle channel and no-bundle options from workload registrations into project-template symbols.
- [ ] 3.9 Add `func.host.json` aliases, descriptions, choices, required status, and visibility for every migrated project-template parameter.
- [ ] 3.10 Ensure every project template declares workload constraints, a primary output, and a mandatory trusted configuration action and does not contain `.func/config.json` directly.
- [ ] 3.11 Add template authoring and instantiation tests for every canonical stack-language combination.
- [ ] 3.12 Ensure setup or package bootstrap flows make default project templates available for each installed stack.

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

## 6. Template Selection

- [x] 6.1 Prototype when filters are checked and when the template context is fixed for conditional topology, and record the decision in this design.
- [ ] 6.2 Define immutable selection models for project-template groups, variants, and their projected configuration actions.
- [ ] 6.3 Apply `--stack` and `--language` to every active project configuration, matching canonical names and `IProjectStack` aliases case-insensitively.
- [ ] 6.4 Keep mixed-stack and mixed-language templates available when the corresponding filter is absent.
- [ ] 6.5 Enforce project type with wrong-type diagnostics and report unknown filters, stack-language conflicts, and whole-template conflicts without substituting another template.
- [ ] 6.6 Narrow groups and their variants by `--stack` and `--language` to templates whose unconditional projects all match every supplied filter and that declare at least one project matching them all.
- [ ] 6.7 Add filter tests for homogeneous, mixed, and conditional templates, grouped variants, both filters together, templates that declare no project, alias matching, and conflicts.

## 7. Selection and Prompting

- [ ] 7.1 Honor every explicit template, stack, and language filter without prompting for substitutes.
- [ ] 7.2 Auto-select any template group or variant with exactly one remaining value, except that an interactive run shows the group picker while unavailable groups are listed.
- [ ] 7.3 Prompt for the project-template group first when no template was supplied, then only for unresolved variants and required parameters.
- [ ] 7.4 Avoid special-casing the basic template name and auto-select only when one applicable group remains.
- [ ] 7.5 Fail non-interactively with the remaining template references or identities whenever a template or variant prompt would be required.
- [ ] 7.6 Preserve cancellation through every selection prompt and return no partially selected mutable state.

## 8. Template Context and Parsing

- [ ] 8.1 Create the init template context from the target directory without a stack, language, or bundle.
- [ ] 8.2 Leave bundle ID and version unavailable during project initialization and test bundle-dependent constraints fail closed.
- [ ] 8.3 Create one command-scoped `Templater` before listing project templates and reuse it through selection, parsing, dry-run, and invocation.
- [ ] 8.4 Form a picked group from the entries the `Templater` listed, and resolve an explicit `--template` reference once through the same `Templater` with type-aware resolution.
- [ ] 8.5 Reuse the `func new` candidate parser and alias coordinator for project-template symbols.
- [ ] 8.6 Parse raw template tokens independently for every remaining variant and distinguish invalid explicit input from unresolved required input.
- [ ] 8.7 Filter argument-compatible identities before applying highest remaining precedence.
- [ ] 8.8 Prompt only unresolved visible required symbols and fail non-interactively with every missing effective alias.
- [ ] 8.9 Perform the final canonical reparse after prompted values and pass only canonical symbol mappings to invocation.

## 9. Force, Dry-Run, and Invocation

- [ ] 9.1 Plan deletion of all target content except `.git` for forced reinitialization.
- [ ] 9.2 Confirm destructive cleanup interactively and treat non-interactive `--force` as explicit authorization.
- [ ] 9.3 Complete template, filter, parsing, primary-output, configuration-action, and combined effect preflight before deleting target content.
- [ ] 9.4 Invoke the selected project `ResolvedTemplate` with target path, canonical symbols, name, conflict policy, and create or dry-run mode.
- [ ] 9.5 Combine forced cleanup, project-template, configuration-finalization, and ordinary post-action effects in deterministic execution order.
- [ ] 9.6 Reconcile dry-run changes following planned cleanup so deletion and recreation are represented accurately.
- [ ] 9.7 Ensure dry-run creates no directories, writes no project or configuration files, and executes no configuration or ordinary post-actions.
- [ ] 9.8 Execute mandatory configuration actions in declared order after scaffolding and run ordinary post-actions only after all configuration succeeds.
- [ ] 9.9 Report partial initialization without deleting generated files or successful prior configurations if a configuration action fails.
- [ ] 9.10 Dispose the command-scoped `Templater` and propagate cancellation through preflight, creation, configuration finalization, and ordinary post-actions.

## 10. Rendering and Outcomes

- [ ] 10.1 Add func-owned init outcomes for no stacks, duplicate stacks, incompatibility, wrong template type, missing packages, ambiguity, invalid arguments, and restricted templates.
- [ ] 10.2 Render template and variant prompts using canonical values and user-facing display labels.
- [ ] 10.3 Render no-template guidance with any supplied filters, the browse URL, a `func new install` next action, and each distinct call to action once when installed templates cannot be used.
- [ ] 10.4 Render ordered plain dry-run effects for cleanup, project files, action-planned `.func/config.json`, and ordinary post-actions.
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
- [ ] 11.7 Remove Java workload project-file generation code migrated to templates.
- [ ] 11.8 Remove initializer dependencies from `func new`, template option hydration, and language group resolution in favor of `IProjectStack` or template-owned metadata.
- [ ] 11.9 Remove legacy initializer fallback from `InitCommand` and dependency injection registration.

## 12. Unit and Integration Tests

- [ ] 12.1 Test static init parsing for path, stack, language, template, force, non-interactive, dry-run, and raw template tokens.
- [ ] 12.2 Test duplicate stack IDs, aliases, one-to-many language ownership, and no-installed-stack guidance.
- [ ] 12.3 Test project-template type validation, item-type diagnostics, and mixed templates without a language tag.
- [ ] 12.4 Test template-first selection with no filters, stack or language filters, explicit templates, and mixed-stack templates.
- [ ] 12.5 Test auto-selection, including a lone applicable group beside unavailable ones, interactive prompting order, and every non-interactive ambiguity diagnostic.
- [ ] 12.6 Test shared template parsing, alias collisions, invalid input, missing required values, canonical mappings, and precedence timing.
- [ ] 12.7 Test that stack and language host bindings are unavailable for single-stack and mixed templates, and that bundle defaults are unavailable.
- [ ] 12.8 Test empty, initialized, adoptable, healable, forced, and declined-force state paths.
- [ ] 12.9 Test configuration actions always persist canonical stack and language and project template content never owns `.func/config.json`.
- [ ] 12.10 Test resolved primary-output paths and action-planned configuration effects for dry-run and actual invocation.
- [ ] 12.11 Test forced dry-run cleanup ordering, `.git` preservation, overlap rejection, and unchanged filesystem.
- [ ] 12.12 Test mandatory configuration ordering, default ordinary post-action execution, dry-run suppression, configuration-failure suppression, and cancellation.
- [ ] 12.13 Test missing applicable templates never fall back to workload scaffolding.
- [ ] 12.14 Add end-to-end initialization coverage for every in-repository stack-language project template.

## 13. Documentation and Validation

- [ ] 13.1 Update `func init --help` for project-template selection, non-interactive behavior, dry-run, and template-specific options.
- [ ] 13.2 Document installed stack and project-template requirements, whole-template filters, and `func new install` guidance.
- [ ] 13.3 Document the primary-output/configuration-action authoring contract, CLI ownership of `.func/config.json`, mandatory canonical stack and language, and that `func:stack` and `func:language` are unavailable to project templates.
- [ ] 13.4 Document destructive force behavior and ordered dry-run effects.
- [ ] 13.5 Document the breaking `IProjectInitializer` to `IProjectStack` workload migration.
- [ ] 13.6 Run targeted abstraction, workload, init command, template integration, parser, renderer, and project-template tests.
- [ ] 13.7 Run restore, the clean Release build with warnings treated as errors, and the full test suite.
- [ ] 13.8 Keep fixture-based CLI regression suites separate from switch qualification. Before removing initializer fallback, verify default first-party project template packages are published and installable for every supported stack and pass baseline init smoke scenarios on fresh and upgraded machines against those published packages.
