## 1. Context and Engine Lifetime

- [ ] 1.1 Add immutable template engine context models for command directory, resolved project stack and language, and resolved extension bundle identity and version.
- [ ] 1.2 Add a dependency-injected templater factory that creates one command-scoped `Templater` and internal engine session from the validated context.
- [ ] 1.3 Replace the misleading bundle host parameter names with `func:bundle-id` and `func:bundle-version`, add the remaining func context parameters, and return the resolved command directory for `WorkingDirectory`.
- [ ] 1.4 Add tests proving separate command scopes do not leak context and all phases within one scope share the same host defaults and engine environment.

## 2. Func-Owned Template Models

- [ ] 2.1 Add func-owned catalog and candidate models, including immutable parameter definitions with canonical name, effective `longName`, optional `shortName`, validation details, and visibility without exposing TemplateEngine types.
- [ ] 2.2 Add typed resolution and invocation outcomes for not found, restricted, constraint failure, ambiguous group, unsatisfied language, ambiguous variant, invalid arguments, file conflict, and success.
- [ ] 2.3 Add the internal func host metadata reader and projection layer that parses the selected `.template.config/func.host.json`, joins `symbolInfo` to canonical parameter symbols, and stores the projected definitions on each candidate.
- [ ] 2.4 Add projection tests for long-name overrides, canonical-name fallback, short-name suppression, hidden parameters, always-visible parameters, hidden templates, malformed host metadata, non-parameter symbols, and absent package metadata.

## 3. Catalog and Constraint Evaluation

- [ ] 3.1 Centralize constraint evaluation so listing and resolution share one fail-closed representation of eligible, restricted, not-evaluated, and failed constraints.
- [ ] 3.2 Update `Templater.ListAsync` to return every installed template with current-context eligibility and diagnostics.
- [ ] 3.3 Add tests for mixed eligible and restricted catalogs, unevaluable constraints, exact context values, and listing templates hidden by host metadata.

## 4. Template Group Resolution

- [ ] 4.1 Implement full-identity-first and case-insensitive exact-short-name matching with ungrouped identities treated as singleton groups.
- [ ] 4.2 Make constraint evaluation mandatory before creating invocation-ready `ResolvedTemplate` items while retaining rejected-template diagnostics outside the group's item list.
- [ ] 4.3 Implement `TemplateGroup` as an immutable `IReadOnlyList<ResolvedTemplate>` with stable ordering, indexed access, enumeration, and filters that preserve item symbol definitions and diagnostics.
- [ ] 4.4 Implement explicit language, validated-argument, and highest-precedence filters without adding an automatic final-selection or ambiguity policy.
- [ ] 4.5 Add tests for identity precedence, short-name matching, multiple groups, restricted-only matches, list behavior, immutable filtering, stable ordering, and zero, one, or multiple remaining items.

## 5. Resolved Template Invocation

- [ ] 5.1 Add `TemplateInvocationRequest` and `TemplateInvocationResult` models for output location, canonical symbol values, file-conflict policy, file changes, and post-actions.
- [ ] 5.2 Add the internal command-scoped invocation service that adapts the selected template and func-owned request to `TemplateCreator`.
- [ ] 5.3 Implement `ResolvedTemplate.Parameters` and `InvokeAsync` so every eligible group item exposes its immutable symbol details, carries its invocation service, and cannot be publicly constructed from an unchecked template.
- [ ] 5.4 Enforce cancellation, disposed-scope failure, and file-conflict-only force behavior without bypassing approved constraints.
- [ ] 5.5 Add invocation tests for canonical parameter values, exact candidate selection, successful output projection, cancellation, file conflicts, engine failures, and invocation after disposal.

## 6. Command Integration and Strict Parsing

- [ ] 6.1 Update the `func new` execution path to resolve command directory, project, language, and bundle context before creating `Templater`.
- [ ] 6.2 Add a DI-registered command-layer `ITemplateArgumentParser` and replace best-effort dynamic option hydration with item-specific second-stage parsing based only on each `ResolvedTemplate.Parameters` collection.
- [ ] 6.3 Reject unknown aliases, missing values, invalid types or choices, and missing required parameters before resolving or invoking a template.
- [ ] 6.4 Narrow the immutable template group using command-selected filters, then invoke the sole remaining item, report ambiguity, or prompt according to command policy.
- [ ] 6.5 Render func-owned listing, resolution, parsing, and invocation outcomes through `IInteractionService` with targeted next actions.
- [ ] 6.6 Add command tests for valid dynamic parameters, invalid argument categories, restricted templates, group and variant ambiguity, file conflicts, cancellation, and successful creation.

## 7. Authoring Contract and Validation

- [ ] 7.1 Document the stable func host parameters, canonical value expectations, opt-in bind-symbol usage, and `.template.config/func.host.json` alias and visibility behavior.
- [ ] 7.2 Add template fixtures that exercise func context bind symbols, bundle constraints, host-specific aliases, visibility, language variants, precedence, and malformed constraints.
- [ ] 7.3 Run targeted template engine and `func new` tests, then restore, build, and test the complete solution with warnings treated as errors.
