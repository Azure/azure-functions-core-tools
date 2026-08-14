## 1. Reconcile Focused Contracts

- [ ] 1.1 Update the pending `func-init-execution` proposal, design, and specification to use template-first selection, whole-template filters, and action-owned multi-project configuration.
- [ ] 1.2 Finalize the `template-engine-constraints` result contract for unavailable summaries, diagnostics, and customer calls to action consumed by `func init`.
- [ ] 1.3 Finalize the `template-engine-post-actions` contract for trusted project configuration actions, catalog projection, resolved primary-output references, dry-run metadata, and mandatory execution.
- [ ] 1.4 Update the Azure-Samples pipeline artifacts so synthesized single-project templates declare configuration actions and multi-project repositories require authored topology.

## 2. Project Topology Metadata

- [ ] 2.1 Add func-owned models for projected and resolved Functions project configurations without exposing TemplateEngine post-action implementation types to commands.
- [ ] 2.2 Project trusted configuration-action metadata into context-free installed project-template catalog entries.
- [ ] 2.3 Resolve active configuration actions against final template parameters and final primary-output paths.
- [ ] 2.4 Derive homogeneous prospective stack and language values while leaving mixed values unavailable.
- [ ] 2.5 Add unit tests for single-project, multi-project, mixed, conditional, renamed, and unavailable topology projections.

## 3. Template-First Selection

- [ ] 3.1 Replace stack-first init creation selection with installed `tags.type = project` group selection followed by variant and parameter resolution.
- [ ] 3.2 Preserve exact identity-before-short-name matching and deterministic non-interactive ambiguity errors for explicit templates.
- [ ] 3.3 Apply explicit `--stack` and `--language` as all-project filters over active resolved configurations.
- [ ] 3.4 Keep restricted installed templates in interactive candidates while preventing their selection.
- [ ] 3.5 Add selection tests for homogeneous and heterogeneous templates, conditional projects, explicit filter conflicts, restricted templates, and non-interactive input.

## 4. Discovery and Remediation UX

- [ ] 4.1 Add the stable Functions-owned quickstart browse URL to the CLI-owned options or constants boundary.
- [ ] 4.2 Render installed project templates, unavailable summaries, and the browse URL through `IInteractionService`.
- [ ] 4.3 Render structured constraint diagnostics and calls to action outside the picker for restricted explicit and interactive selections.
- [ ] 4.4 Return browse and `func new install` guidance without package mutation when an explicit template is not installed.
- [ ] 4.5 Add command tests for empty catalogs, unknown templates, unavailable templates, browse guidance, themed output, and item-template rejection.

## 5. Configuration Preflight and Dry-Run

- [ ] 5.1 Validate the selected template has at least one mandatory trusted configuration action with canonical stack and language.
- [ ] 5.2 Validate primary-output references, target containment, direct project-root anchors, unique project roots, and configuration output collisions.
- [ ] 5.3 Reject template file effects that create or modify `.func/config.json`.
- [ ] 5.4 Add planned `.func/config.json` writes to combined creation effects and dry-run rendering.
- [ ] 5.5 Ensure every selection, constraint, topology, and effect check completes before destructive `--force` cleanup.
- [ ] 5.6 Add preflight and dry-run tests for malformed actions, missing or renamed outputs, duplicate roots, path traversal, collisions, conditional projects, and forced initialization.

## 6. Project Configuration Finalization

- [ ] 6.1 Add a command-scoped finalization service that derives each project root from its resolved primary-output file and writes the current CLI configuration schema atomically.
- [ ] 6.2 Validate the generated primary-output anchor exists and the installed workload recognizes each action's canonical stack and language before writing that project configuration.
- [ ] 6.3 Execute configuration actions in declared order after scaffolding and before every ordinary post-action.
- [ ] 6.4 Stop on the first configuration failure, preserve generated content and prior writes, skip ordinary post-actions, and return a partial-initialization result identifying the failed project.
- [ ] 6.5 Add finalization tests for one and multiple projects, nested roots, atomic writes, ordering, ordinary post-actions, and partial failures.

## 7. Init Orchestration and Adoption

- [ ] 7.1 Wire template-first selection, workload eligibility, topology preflight, template invocation, configuration finalization, and ordinary post-actions into the init creation path.
- [ ] 7.2 Keep existing root-project adoption and healing metadata-only and reject `--template` without `--force`.
- [ ] 7.3 Preserve non-recursive behavior for existing directories containing nested Functions projects.
- [ ] 7.4 Render success with the selected template and every configured project root through standard init result models.
- [ ] 7.5 Add end-to-end command tests for creation, adoption, healing, force, cancellation, dry-run, multi-project success, and partial initialization.

## 8. Template Authoring and Documentation

- [ ] 8.1 Migrate built-in project templates to declare workload constraints, primary outputs, and one configuration action per Functions project.
- [ ] 8.2 Add representative homogeneous, heterogeneous, conditional, restricted, and malformed project-template fixtures.
- [ ] 8.3 Document installed quickstart discovery, explicit package installation, whole-template filters, multi-project behavior, and the browse URL in `func init` help and user documentation.
- [ ] 8.4 Document the primary-output and configuration-action authoring contract for template publishers.
- [ ] 8.5 Run targeted init and TemplateEngine tests, then complete repository restore, release build, and test validation.
