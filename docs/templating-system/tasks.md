## Current P0 implementation status

The [current design](design.md) separates implementation from historical
planning-artifact readiness.

- [x] Add an optional runtime-owned facts seam at the resolved project boundary.
- [x] Add strict, immutable `func-project-capabilities` item requirements.
- [x] Add generic Compatible / Incompatible / Unknown matching with immutable evidence binding.
- [x] Add read-only revalidation that invalidates changed evidence or requirements.
- [x] Add focused contract, cancellation, mutation, neutrality and dependency-boundary tests.
- [ ] Implement and qualify production runtime-owned facts.
- [ ] Implement exact immutable package/reference intake and private engine registration.
- [ ] Integrate strict declaration extraction, discovery and selection with semantic admission.
- [ ] Implement actual-output capture and exact CLI configuration preparation.
- [ ] Implement a single-use, create-only prepared operation without rerender or reserialization.
- [ ] Integrate one candidate `init`/`new` graph and prove previous-route/artifact exclusion.
- [ ] Qualify selected project/item content, an unfamiliar runtime, and the runtime journey.

Local unit validation of the foundation does not complete independent
implementation review or end-to-end qualification. No production runtime or
public command adopts the new seam in this change.

## Historical planning checklist (preserved)

The checklist below tracks the earlier broad program. Checked specification
artifacts are not completed product capabilities; its lifecycle and Quickstart
scope is not a P0 implementation dependency.

## 1. Existing Focused Specifications

- [x] 1.1 Track the completed `template-engine-integration` planning artifacts and its core integration responsibility.
- [x] 1.2 Track the completed `template-package-install` planning artifacts and its package lifecycle responsibility.
- [x] 1.3 Track the completed `func-new-execution` planning artifacts and its item-template command responsibility.
- [x] 1.4 Track the completed `func-init-execution` planning artifacts and its project-template command responsibility.

## 2. Planned Focused Specifications

- [x] 2.1 Create and strictly validate the `template-engine-constraints` change covering func-specific bundle, stack, and related compatibility constraints.
- [ ] 2.2 Create and strictly validate the `template-engine-post-actions` change covering supported actions, execution policy, dry-run behavior, cancellation, and diagnostics.
- [ ] 2.3 Create and strictly validate the `template-engine-bind-sources` change covering MSBuild, npm, and other project-ecosystem value sources.
- [ ] 2.4 Create and strictly validate the `func-new-search` change covering NuGet feed scanning, `FuncTemplate` discovery manifests, CDN publication, and CLI search.
- [x] 2.5 Create and strictly validate the `azure-samples-template-pipeline` change covering repository conventions, validation, package generation, and seamless release automation.
- [x] 2.6 Create and strictly validate the `func-init-quickstarts` change covering first-class Azure-Samples quickstart discovery, selection, acquisition, and invocation.

## 3. Cross-Change Coordination

- [ ] 3.1 Reconcile the ten focused changes so every detailed requirement has one authoritative owner.
- [ ] 3.2 Add explicit dependency references wherever a focused change consumes a contract owned by another change.
- [ ] 3.3 Resolve conflicting terminology, context models, result shapes, and lifecycle assumptions across all focused designs.
- [ ] 3.4 Update the umbrella inventory after any focused change is renamed, split, added, or removed.
- [ ] 3.5 Strictly validate all ten focused changes and this umbrella change before declaring the templating system fully specified.
- [ ] 3.6 Assign companion-template acquisition to a focused change and update `func-init-execution` and `template-package-install` with the result.
