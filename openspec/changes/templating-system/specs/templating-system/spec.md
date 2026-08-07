## Purpose

Coordinates the focused specifications that together define the func templating system, including their ownership boundaries, dependencies, readiness, and overall completion criteria.

## ADDED Requirements

### Requirement: Focused change inventory
The templating system SHALL be divided across these focused OpenSpec changes: `template-engine-integration`, `template-package-install`, `func-init-execution`, `func-new-execution`, `template-engine-constraints`, `template-engine-post-actions`, `template-engine-bind-sources`, `func-new-search`, `azure-samples-template-pipeline`, and `func-init-quickstarts`. The umbrella change SHALL track whether each focused change has complete planning artifacts without treating artifact completion as implementation completion.

#### Scenario: Existing focused change is tracked
- **WHEN** a focused change has complete proposal, specification, design, and task artifacts
- **THEN** the umbrella tracker identifies its specification artifacts as complete

#### Scenario: Planned focused change has not been created
- **WHEN** a listed focused change does not yet exist
- **THEN** the umbrella tracker identifies it as planned rather than omitting it or treating it as implemented

#### Scenario: Focused change implementation begins
- **WHEN** implementation tasks start for a focused change
- **THEN** its implementation progress remains distinct from its specification-artifact status

### Requirement: Focused ownership boundaries
Each focused change SHALL own detailed requirements for its assigned capability. The umbrella change SHALL own only the inventory, responsibility boundaries, dependency map, and program-level completion criteria, and SHALL NOT duplicate detailed behavior from focused specifications.

#### Scenario: Core integration behavior is specified
- **WHEN** behavior concerns the command-scoped TemplateEngine environment, catalog, resolution models, or invocation boundary
- **THEN** `template-engine-integration` is the authoritative focused change

#### Scenario: Package lifecycle behavior is specified
- **WHEN** behavior concerns template package installation, update, uninstall, source selection, ownership, or replacement safety
- **THEN** `template-package-install` is the authoritative focused change

#### Scenario: Command execution behavior is specified
- **WHEN** behavior concerns item-template execution through `func new` or project-template execution through `func init`
- **THEN** `func-new-execution` or `func-init-execution`, respectively, is the authoritative focused change

#### Scenario: Extensibility behavior is specified
- **WHEN** behavior concerns func-specific engine constraints, post-actions, or bind sources
- **THEN** `template-engine-constraints`, `template-engine-post-actions`, or `template-engine-bind-sources`, respectively, is the authoritative focused change

#### Scenario: Template discovery behavior is specified
- **WHEN** behavior concerns scanning NuGet feeds for `FuncTemplate` packages, producing a searchable manifest, or publishing that manifest to the CDN
- **THEN** `func-new-search` is the authoritative focused change

#### Scenario: Azure-Samples packaging behavior is specified
- **WHEN** behavior concerns turning Azure-Samples repositories into releasable template packages accepted by `func new install`
- **THEN** `azure-samples-template-pipeline` is the authoritative focused change

#### Scenario: Init quickstart behavior is specified
- **WHEN** behavior concerns first-class `func init` discovery and selection of available Azure-Samples quickstart templates
- **THEN** `func-init-quickstarts` is the authoritative focused change

### Requirement: Cross-change dependencies
Focused changes SHALL declare dependencies on other focused changes whenever they rely on contracts owned elsewhere. Dependency declarations SHALL identify the required contract rather than copying its requirements.

#### Scenario: Command execution uses the integration boundary
- **WHEN** a command execution change requires template discovery, resolution, parsing metadata, or invocation
- **THEN** it references the applicable contract from `template-engine-integration`

#### Scenario: Extensibility component uses engine context
- **WHEN** a constraint, post-action, or bind source requires TemplateEngine registration, context, or invocation services
- **THEN** its focused change identifies the integration contract on which it depends

#### Scenario: Quickstart integration consumes Azure-Samples templates
- **WHEN** `func init` exposes Azure-Samples quickstart templates
- **THEN** `func-init-quickstarts` declares dependencies on `azure-samples-template-pipeline` and `func-init-execution`

#### Scenario: Search indexes installable template packages
- **WHEN** template discovery publishes an entry that users can install
- **THEN** `func-new-search` declares the package compatibility contract it consumes from `template-package-install`

#### Scenario: Child requirements conflict
- **WHEN** focused changes assign incompatible behavior to the same responsibility
- **THEN** the conflict is resolved in the focused change that owns that responsibility before the templating system is considered fully specified

### Requirement: Overall specification readiness
The templating system SHALL be considered fully specified only when all ten focused changes contain complete, strictly valid planning artifacts and their cross-change dependencies are consistent.

#### Scenario: One focused specification is missing
- **WHEN** any listed focused change lacks a required planning artifact
- **THEN** the templating system remains partially specified

#### Scenario: Every focused specification is complete
- **WHEN** all listed focused changes pass strict OpenSpec validation and their dependency boundaries agree
- **THEN** the templating system is ready for coordinated implementation
