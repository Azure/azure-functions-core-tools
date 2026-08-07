## 1. Template Integration Prerequisites

- [ ] 1.1 Add the func-owned `TemplateType` projection for TemplateEngine `tags.type` values and expose it on catalog and resolved-template models.
- [ ] 1.2 Add item-type-scoped template listing and group resolution that excludes project templates before group ambiguity while preserving wrong-type diagnostics.
- [ ] 1.3 Update template group resolution tests for shared short names, exact project-template identities, and item-only listing.
- [ ] 1.4 Refine candidate parameter preparation so invalid explicit input is distinct from unresolved required input that remains eligible for interactive completion.
- [ ] 1.5 Extend template invocation requests and results with create versus dry-run mode and projected creation effects.
- [ ] 1.6 Map dry-run invocation to TemplateEngine's native dry-run API and verify it does not create directories, write files, or execute post-actions.

## 2. Static Command Surface

- [ ] 2.1 Introduce the immutable `NewExecutionRequest` model for host options and preserved raw template tokens.
- [ ] 2.2 Replace the inherited `NewCommand` positional `[path]` argument with optional positional `<template>` and add a `--path` option bound to `WorkingDirectory`.
- [ ] 2.3 Add parser validation that rejects simultaneous positional and `--template` / `-t` selectors.
- [ ] 2.4 Add `--language`, template execution `--path`, and `--dry-run` to the stable command surface while retaining name, force, non-interactive, list, and plain/JSON `--output` options.
- [ ] 2.5 Preserve registered install, update, and uninstall subcommand routing while allowing those names through the explicit template selector.
- [ ] 2.6 Capture the original unmatched template token sequence during Stage A without accepting it as a successful final parse.
- [ ] 2.7 Reduce `NewCommand.ExecuteAsync` to request construction and one runner delegation.

## 3. Candidate Argument Parsing

- [ ] 3.1 Implement deterministic reserved-alias collection from the effective `func new`, help, global, lifecycle, and subcommand surfaces.
- [ ] 3.2 Implement long-name fallback to `--param:<canonical-name>` and explicit short-name fallback to `-p:<short-name>` without generating short aliases.
- [ ] 3.3 Implement `ITemplateArgumentParser` using ephemeral candidate-specific System.CommandLine parsers built from projected parameter definitions.
- [ ] 3.4 Parse the same raw token sequence independently for every remaining candidate and return canonical values, invalid-input diagnostics, and unresolved required parameters.
- [ ] 3.5 Reject unknown aliases, missing option values, invalid choices, and invalid types without discarding diagnostic context.
- [ ] 3.6 Reparse the selected candidate after prompt values are merged and use that result as the authoritative canonical symbol map.
- [ ] 3.7 Render template-specific help from effective aliases and projected metadata without attaching dynamic options to the singleton command graph.

## 4. Project Context and Templater Lifetime

- [ ] 4.1 Resolve `--path` as the requested template execution directory, defaulting to process current directory without requiring it to be the project root.
- [ ] 4.2 Discover the containing Functions project by walking upward from the execution directory or its nearest existing ancestor.
- [ ] 4.3 Resolve stack, explicit language override or project language fallback, and extension bundle identity and version into canonical values.
- [ ] 4.4 Populate template working directory and discovered project root as distinct immutable context values.
- [ ] 4.5 Return actionable `func init` guidance when no Functions project exists in the execution path hierarchy.
- [ ] 4.6 Create one `Templater` from the immutable context and reuse it through listing, resolution, parsing, prompting, dry-run, and creation.
- [ ] 4.7 Dispose the command-scoped `Templater` after listing or invocation and pass cancellation through every asynchronous operation.

## 5. Item Template Selection

- [ ] 5.1 Use item-type-scoped catalog listing for `func new --list` and ordinary interactive discovery.
- [ ] 5.2 Prompt an interactive user to select an item-template family when no selector is supplied, and fail with explicit-selector guidance in non-interactive mode.
- [ ] 5.3 Resolve positional and explicit references with `TemplateType.Item`, preserving not-found, wrong-type, restricted, and ambiguous-group diagnostics.
- [ ] 5.4 Apply explicit language before project language fallback and report available languages when filtering leaves no candidate.
- [ ] 5.5 Parse candidate arguments and narrow the immutable group to argument-compatible identities before considering precedence.
- [ ] 5.6 Apply highest precedence only after explicit and argument-based filtering is complete.
- [ ] 5.7 Resolve one surviving candidate directly, prompt for genuine remaining ambiguity, and report candidates instead when interaction is disabled.

## 6. Required Parameter Completion

- [ ] 6.1 Prompt only for visible unresolved required parameters using effective aliases, defaults, data types, and choices.
- [ ] 6.2 Leave optional parameters unset so TemplateEngine defaults remain authoritative.
- [ ] 6.3 Report all unresolved required aliases in one diagnostic when `--non-interactive` is supplied or the terminal cannot prompt.
- [ ] 6.4 Treat hidden required parameters without resolvable defaults as template-authoring failures rather than prompting for undiscoverable input.
- [ ] 6.5 Reject invalid explicit values without prompting for replacement values.

## 7. Output and Invocation

- [ ] 7.1 Resolve omitted `--path` to process current directory, relative paths from process current directory, and absolute paths without rebasing.
- [ ] 7.2 Pass the requested execution path as TemplateEngine working and output directory while retaining the discovered ancestor as `func:project-root`.
- [ ] 7.3 Build `TemplateInvocationRequest` from the selected template, canonical symbol values, execution path, file-conflict policy, and invocation mode.
- [ ] 7.4 Map `--force` only to destructive file-conflict permission and preserve every type, constraint, parsing, and selection gate.
- [ ] 7.5 Invoke the selected `ResolvedTemplate` for both create and dry-run modes without passing its identity back to `Templater`.
- [ ] 7.6 Render dry-run create, modify, delete, and post-action effects with an explicit preview label in plain and JSON formats.
- [ ] 7.7 Render successful creation, destructive conflicts, and known invocation failures from func-owned result models.

## 8. Legacy Path Removal and DI

- [ ] 8.1 Remove `NewCommandArgPreparer` and its pre-parse call from `Program`.
- [ ] 8.2 Remove dynamic option attachment, unmatched-token key/value walking, and silent hydration catches from `NewCommand`.
- [ ] 8.3 Replace `TemplateOptionHydrator` usage with projected `ResolvedTemplate.Parameters` and strict candidate parsing.
- [ ] 8.4 Replace legacy template workload and provider-registry execution dispatch in `NewCommandRunner` with `ITemplaterFactory`.
- [ ] 8.5 Remove or adapt `LanguageTiebreaker` so explicit language is authoritative and prompting occurs only after all deterministic filters.
- [ ] 8.6 Register the new request orchestration, argument parser, alias assignment, prompting, and rendering dependencies with the narrowest appropriate lifetimes.
- [ ] 8.7 Remove legacy template-selection types and tests only after all remaining callers have migrated.

## 9. Command and Parser Tests

- [ ] 9.1 Test positional template selection, explicit selection, selector conflict, `--path`, and omitted-selector behavior.
- [ ] 9.2 Test lifecycle subcommand routing and explicit selection of templates named install, update, and uninstall.
- [ ] 9.3 Test item-only listing and resolution, shared project/item short names, and wrong-type exact identity guidance.
- [ ] 9.4 Test reserved long and short alias fallback, empty short aliases, canonical mapping, and template help output.
- [ ] 9.5 Test candidate-specific unknown options, missing values, invalid types, invalid choices, and diagnostics when every candidate fails.
- [ ] 9.6 Test explicit-language authority, project-language fallback, argument filtering before precedence, and ambiguity after precedence.
- [ ] 9.7 Test interactive template selection, required-value prompting, optional-value omission, and invalid explicit values without corrective prompts.
- [ ] 9.8 Test `--non-interactive` and non-interactive terminal failures for missing template selection, ambiguity, and all unresolved required parameters.

## 10. Invocation and Integration Tests

- [ ] 10.1 Test default, relative, absolute, nested, and not-yet-created execution paths independently from discovered project context.
- [ ] 10.2 Test canonical symbol values, name, output, and cancellation passed to the exact selected `ResolvedTemplate`.
- [ ] 10.3 Test destructive conflict refusal without force and permitted creation with force without bypassing constraints.
- [ ] 10.4 Test dry-run file effects and post-actions while asserting the target filesystem remains unchanged.
- [ ] 10.5 Test plain and JSON rendering for list, preview, creation, ambiguity, invalid arguments, and missing input.
- [ ] 10.6 Add end-to-end parser coverage proving Stage A unmatched tokens cannot reach a success path without strict Stage B validation.
- [ ] 10.7 Add consecutive-command coverage proving separate project contexts create separate engine environments without stale host defaults.

## 11. Documentation and Validation

- [ ] 11.1 Update `func new --help` descriptions and examples for positional templates, template execution `--path`, rendering `--output`, `--non-interactive`, and `--dry-run`.
- [ ] 11.2 Document the `--template` escape hatch for reserved lifecycle names and exact identities.
- [ ] 11.3 Document that project discovery walks upward from `--path` and that generated files remain rooted at the requested execution directory.
- [ ] 11.4 Update user-facing template documentation for strict template options, output-path semantics, preview behavior, and breaking grammar changes.
- [ ] 11.5 Run targeted command, parser, template integration, and renderer tests.
- [ ] 11.6 Run restore, the clean Release build with warnings treated as errors, and the full test suite.
