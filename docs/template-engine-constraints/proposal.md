## Why

Templates need a way to say what they require before a command runs them. `func init` must not scaffold a project whose stack workloads aren't installed, and `func new` must not add a function that the project's extension bundle can't support. Microsoft.TemplateEngine already evaluates template constraints, but its built-in workload and SDK checks describe the .NET SDK. The other templating changes defer the func-specific checks, their results, and the guidance users see to this change.

## What Changes

- Add a `func-workload` constraint that requires workloads loaded by the CLI, optionally within a version range.
- Add a `func-bundle` constraint that requires the project's resolved extension bundle, optionally with an identity and version range.
- Keep the engine's `os` and `host` constraints, and stop registering the .NET SDK `sdk-version` and `workload` constraints.
- Map constraint results to the four eligibility states in `template-engine-integration`, with a reason for each restriction and, where one exists, a CLI-generated next step.
- Limit func constraint types to func template packages, and define how Azure-Samples packages declare workload requirements.
- Validate raw constraint declarations in packaging, isolated package preflight, and installed-template listing and resolution; reject changed or unreadable selected-template configuration before invocation.
- Keep target-framework-aware template selection out of constraints.

## Capabilities

### New Capabilities

- `template-constraints`: Func-specific template constraints, their declaration and evaluation, and the structured restriction results commands render.

### Modified Capabilities

- `template-package-install`: Reject malformed raw constraint declarations before live hive mutation, including third-party and folder packages.
- `template-execution`: Validate current mounted declarations and preserve the selected template while rejecting changed or unreadable configuration before creation.
- `func-new-execution`: Resolve required extension bundles before creating the engine environment or listing templates, while preserving projects without bundles.

## Impact

- Registers func constraint factories with the per-command template engine session.
- Supplies the constraint results consumed by `template-engine-integration`, `func-new-execution`, `func-init-execution`, and `func-init-quickstarts`.
- Defines the workload constraint form that `azure-samples-template-pipeline` validates and synthesizes.
- Replaces the templates workload's single minimum bundle version check with per-template bundle constraints when `func new` moves to the new runtime.
- Does not install, update, or acquire workloads or template packages.
