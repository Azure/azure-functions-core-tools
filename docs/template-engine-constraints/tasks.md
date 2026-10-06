## 1. Constraint Types

- [ ] 1.1 Add the `func-workload` constraint factory over the usable workload snapshot and installed workload list, excluding workloads rejected by applicable compatibility or activation checks, retaining their CLI-owned diagnostics, validating names, matching aliases and package IDs case-insensitively, and checking NuGet version ranges with the workload catalog's prerelease rule.
- [ ] 1.2 Add the `func-bundle` constraint factory that compares its optional identity and version range with `func:bundle-id` and `func:bundle-version`.
- [ ] 1.3 Register the func constraint factories with each command's engine session through dependency injection, and leave out the .NET SDK `sdk-version` and `workload` factories.
- [ ] 1.4 Add tests for string, object, alternative, and missing arguments, names with spaces, leading dashes, or a `.nupkg` suffix, published IDs of platform-specific workloads, bare, bracketed, floating, prerelease, and invalid ranges, runtime and content workload versions, installed workloads rejected by compatibility checks or unable to load, missing and mismatched bundles, `os` and `host` results, and evaluation without project context. Include a preview workload satisfying a stable numeric minimum and a preview CLI failing a stable `host` minimum.

## 2. Evaluation Results

- [ ] 2.1 Within the integration's shared evaluation, classify not-evaluated engine results as `NotEvaluated` for types the session doesn't register, matched case-sensitively, and as `Failed` otherwise.
- [ ] 2.2 Keep one diagnostic per unmet constraint with its type, message, and optional next step, choose the most severe state for the template, and derive its summary.
- [ ] 2.3 Build next steps and fixed guidance in one place, including `func setup` for workloads that belong to a setup feature, and test them against the workload and setup command options. Preserve activation diagnostics, do not promise automatic compatible-version selection, and name a compatible replacement only when known without broadening version or profile pins.
- [ ] 2.4 Add tests for every state, several unmet constraints, unmet alternatives, `--force`, and messages containing console markup or control characters.

## 3. Authoring and Migration

- [ ] 3.1 Document the func constraint types, arguments, version rules, case-sensitive type names, unique object entries, and the shared-package rule in the template authoring guidance.
- [ ] 3.2 Add one context-independent raw declaration validator shared by packaging, isolated template install/update preflight, and installed-template listing and resolution. Reject repeated top-level `constraints` members and duplicate properties in every object in the constraints subtree before normalization, along with malformed sections, entries, types, and func arguments. Evaluate the validated content snapshot rather than stale engine cache data. Enforce the shared-package rule from available NuGet host-sharing metadata in packaging, preflight, and installed-template validation, without classifying metadata-free folders as shared.
- [ ] 3.3 Replace the templates workload's minimum bundle version check with `func-bundle` constraints when `func new` moves to the new runtime.
- [ ] 3.4 Add tests for third-party and folder packages, rejected replacement leaving the live hive unchanged, repeated root `constraints`, labels, `type`, `args`, nested `id`/`version`, distinct alternative objects, malformed declarations hidden by normalization or cache, omitted and empty constraints, valid unknown types, prebuilt and installed shared packages with and without func types, and unchanged versus changed or unreadable configuration before invocation. Verify changed configuration blocks the original selection without re-resolution or substitution.
