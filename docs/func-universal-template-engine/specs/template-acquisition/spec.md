## ADDED Requirements

### Requirement: Templates are standard template-engine packages

The CLI SHALL treat function templates as standard Microsoft.TemplateEngine
packages containing `.template.config/template.json`. The CLI SHALL NOT define a
proprietary template DSL or catalog-projection format.

#### Scenario: Package carries template.json

- **WHEN** a template package is installed
- **THEN** each template it contains is described by a `.template.config/template.json`
  file the engine can load directly

#### Scenario: Legacy formats are rejected

- **WHEN** a package uses the retired V2 `NewTemplate[]` DSL or a
  `dotnet-templates.json` projection
- **THEN** the CLI does not interpret it as a template source

### Requirement: Templates install into an isolated func hive

The CLI SHALL host Microsoft.TemplateEngine with a func-owned settings location and
SHALL install template packages into that hive using the engine's
`TemplatePackageManager`. The CLI MUST NOT read from or write to the `dotnet new`
cache (`~/.templateengine/dotnetcli`).

#### Scenario: Install writes to the func hive

- **WHEN** a user installs a template package through the CLI
- **THEN** the package is acquired and mounted under the func-owned settings hive
- **AND** the user's `dotnet new` template cache is unchanged

#### Scenario: dotnet new templates are not visible to func

- **WHEN** a user has templates installed only via `dotnet new`
- **THEN** those templates are not surfaced by `func new`

### Requirement: A single install front-door routes by package type

The CLI SHALL expose one install entry point that routes a package to the engine's
template package manager when it is a template package and to the existing workload
installer when it is a workload package, determined by package type.

#### Scenario: Template package routed to the engine

- **WHEN** a user installs a package whose type marks it as a template package
- **THEN** the CLI installs it via the engine's `TemplatePackageManager`

#### Scenario: Workload package routed to the workload installer

- **WHEN** a user installs a package whose type marks it as a func workload
- **THEN** the CLI installs it via the existing workload installer

### Requirement: Templates can be updated and uninstalled

The CLI SHALL allow installed template packages to be listed, updated, and
uninstalled through the engine's package manager against the func hive.

#### Scenario: Uninstall removes template visibility

- **WHEN** a user uninstalls a previously installed template package
- **THEN** its templates are no longer surfaced by `func new`
