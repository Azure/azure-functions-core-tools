## Why

The func templating redesign spans integration, package lifecycle, command execution, extensibility, and project-tool integration across several focused changes. An umbrella specification is needed to keep their ownership boundaries, dependencies, and completion status coherent without duplicating the detailed requirements in each child change.

## What Changes

- Establish a templating-system roadmap covering these existing focused changes:
  - `template-engine-integration`
  - `template-package-install`
  - `func-init-execution`
  - `func-new-execution`
- Track these focused changes that still need specifications:
  - `template-engine-constraints`
  - `template-engine-post-actions`
  - `template-engine-bind-sources`
  - `func-new-search`
  - `azure-samples-template-pipeline`
  - `func-init-quickstarts`
- Define the responsibility and dependency boundaries between the focused changes.
- Define completion criteria for the overall templating system while keeping detailed behavior authoritative in the focused specifications.
- Use the umbrella change for coordination and sequencing only; product implementation remains in the focused changes.

## Capabilities

### New Capabilities

- `templating-system`: Coordinates the complete func TemplateEngine feature set, focused-change ownership, dependency ordering, and program-level completion criteria.

### Modified Capabilities

None.

## Impact

- Adds an umbrella OpenSpec change spanning all func TemplateEngine work.
- Establishes planned names and scopes for constraints, post-actions, bind sources, template search, Azure-Samples packaging, and init quickstarts.
- Does not directly modify product code or replace requirements in any focused change.
