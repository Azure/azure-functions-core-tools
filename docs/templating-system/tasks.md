## 1. Existing Focused Specifications

- [x] 1.1 Track the completed `template-engine-integration` planning artifacts and its core integration responsibility.
- [x] 1.2 Track the completed `template-package-install` planning artifacts and its package lifecycle responsibility.
- [x] 1.3 Track the completed `func-new-execution` planning artifacts and its item-template command responsibility.
- [x] 1.4 Track the completed `func-init-execution` planning artifacts and its project-template command responsibility.

## 2. Planned Focused Specifications

- [ ] 2.1 Create and strictly validate the `template-engine-constraints` change covering func-specific bundle, stack, and related compatibility constraints.
- [ ] 2.2 Create and strictly validate the `template-engine-post-actions` change covering supported actions, execution policy, dry-run behavior, cancellation, and diagnostics.
- [ ] 2.3 Create and strictly validate the `template-engine-bind-sources` change covering MSBuild, npm, and other project-ecosystem value sources.
- [ ] 2.4 Create and strictly validate the `func-new-search` change covering NuGet feed scanning, `FuncTemplate` discovery manifests, CDN publication, and CLI search.
- [ ] 2.5 Create and strictly validate the `azure-samples-template-pipeline` change covering repository conventions, validation, package generation, and seamless release automation.
- [ ] 2.6 Create and strictly validate the `func-init-quickstarts` change covering first-class Azure-Samples quickstart discovery, selection, acquisition, and invocation.

## 3. Cross-Change Coordination

- [ ] 3.1 Reconcile the ten focused changes so every detailed requirement has one authoritative owner.
- [ ] 3.2 Add explicit dependency references wherever a focused change consumes a contract owned by another change.
- [ ] 3.3 Resolve conflicting terminology, context models, result shapes, and lifecycle assumptions across all focused designs.
- [ ] 3.4 Update the umbrella inventory after any focused change is renamed, split, added, or removed.
- [ ] 3.5 Strictly validate all ten focused changes and this umbrella change before declaring the templating system fully specified.
