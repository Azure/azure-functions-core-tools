## Purpose

Defines the func-specific template constraints, how templates declare them, how the CLI evaluates them, and the structured results commands receive.

## ADDED Requirements

### Requirement: Constraint declaration
Templates SHALL declare func requirements in the TemplateEngine `constraints` section of `template.json`. A template SHALL be eligible only when every declared constraint is satisfied. When a constraint's arguments are an array, satisfying any one item SHALL satisfy that constraint. Func constraint types SHALL require arguments.

#### Scenario: Template declares several constraints
- **WHEN** a template declares a workload constraint and a bundle constraint
- **THEN** it is eligible only when both are satisfied

#### Scenario: Constraint lists alternatives
- **WHEN** a constraint's arguments list two workloads
- **THEN** the constraint is satisfied when either workload meets its requirement

#### Scenario: Func constraint has no arguments
- **WHEN** a `func-workload` constraint omits `args`
- **THEN** the template's evaluation is `Failed`

### Requirement: Func constraint types stay in func template packages
Func constraint types SHALL use the `func-` prefix. A template package that other template hosts also install SHALL NOT declare func constraint types.

#### Scenario: Package is also a dotnet new package
- **WHEN** a template package also declares the `Template` package type
- **THEN** its templates declare no func constraint types

### Requirement: Workload requirements
The `func-workload` constraint SHALL name a workload by alias or package ID with an optional version range. Names SHALL start with an ASCII letter or digit, SHALL contain only ASCII letters, digits, `.`, `-`, and `_`, SHALL NOT end in `.nupkg`, and SHALL compare case-insensitively with the aliases, package IDs, and published package IDs of the workloads the CLI loaded when the command started. The loaded workloads SHALL be the highest installed version of each runtime workload that loaded and every installed version of each content workload except packages built for another platform. The constraint SHALL be satisfied when a matching loaded workload has a version in the range. When a matching workload is installed but didn't load, the constraint SHALL be restricted without a next step. Evaluation SHALL NOT access the network or depend on project context.

#### Scenario: Required workload is loaded
- **WHEN** a template requires `node` and a loaded `node` workload has a version in the declared range
- **THEN** the constraint is satisfied

#### Scenario: Required workload is missing
- **WHEN** no installed workload matches the required name
- **THEN** the template is restricted as missing the workload
- **AND** the next step installs it, through `func setup` when the workload belongs to a setup feature

#### Scenario: Required workload is installed but not loaded
- **WHEN** a matching workload is installed but didn't load
- **THEN** the template is restricted without a next step

#### Scenario: Loaded workload is older than required
- **WHEN** every matching loaded version is below a range that has no upper bound
- **THEN** the template is restricted as outdated
- **AND** the next step updates the workload

#### Scenario: Outdated workload's range has an upper bound
- **WHEN** every matching loaded version is below a range that has an upper bound
- **THEN** the template is restricted as outdated without a next step

#### Scenario: Loaded workload is newer than allowed
- **WHEN** every matching loaded version is outside the range and at least one is above it
- **THEN** the template is restricted as incompatible without a next step

#### Scenario: Older in-range version is installed beside a newer runtime workload
- **WHEN** a runtime workload has an in-range version installed and a newer out-of-range version is loaded
- **THEN** the constraint evaluates only the loaded version and is restricted as incompatible

#### Scenario: Workload name isn't valid
- **WHEN** a workload name contains a space, starts with `-`, or ends in `.nupkg`
- **THEN** the template's evaluation is `Failed`
- **AND** no command is generated from the name

#### Scenario: Platform-specific workload is named by its published package ID
- **WHEN** a template names a platform-specific workload by the package ID it was published under
- **THEN** the constraint matches the platform package that loaded

#### Scenario: No project exists yet
- **WHEN** a workload constraint is evaluated before a project context exists
- **THEN** it gives the same result it gives inside a project

### Requirement: Extension bundle requirements
The `func-bundle` constraint SHALL compare an optional bundle identity and version range with the `func:bundle-id` and `func:bundle-version` values of the command context. Identities SHALL compare case-insensitively. When the command resolved no bundle, the constraint SHALL be restricted. Project templates SHALL NOT declare `func-bundle`.

#### Scenario: Resolved bundle satisfies the range
- **WHEN** the project's resolved bundle version is in the declared range
- **THEN** the constraint is satisfied

#### Scenario: Resolved bundle is older than required
- **WHEN** the resolved bundle version is below the declared range
- **THEN** the template is restricted as needing a newer bundle
- **AND** the next step explains how to allow or install a newer bundle without running a workload command

#### Scenario: Bundle identity differs only by case
- **WHEN** the declared identity matches the resolved identity ignoring case
- **THEN** the identity requirement is satisfied

#### Scenario: Resolved bundle has another identity
- **WHEN** the constraint names a bundle identity that differs from the resolved one
- **THEN** the template is restricted without a next step

#### Scenario: New project has no bundle
- **WHEN** `func init` evaluates a bundle constraint
- **THEN** the constraint is restricted and cannot be satisfied

### Requirement: Version ranges
Func constraint versions SHALL use NuGet version range syntax, including floating versions, where a bare version is a minimum and an omitted version accepts any version. A prerelease version SHALL also satisfy a range when its release version is in the range. An invalid range SHALL make the constraint fail.

#### Scenario: Bare version
- **WHEN** a constraint declares version `1.2`
- **THEN** versions 1.2.0 and later satisfy it

#### Scenario: Bracketed range
- **WHEN** a constraint declares `[1.2,2.0)`
- **THEN** versions from 1.2.0 up to but excluding 2.0.0 satisfy it

#### Scenario: Prerelease of the minimum
- **WHEN** a constraint declares `1.0` and version `1.0.0-preview.2` is loaded
- **THEN** the constraint is satisfied

#### Scenario: Floating range
- **WHEN** a constraint declares `[4.*, 5.0.0)`
- **THEN** versions from 4.0.0 up to but excluding 5.0.0 satisfy it

#### Scenario: Invalid range
- **WHEN** a constraint declares a version that isn't a valid range
- **THEN** the template's evaluation is `Failed`

### Requirement: Built-in constraint types
The CLI SHALL evaluate the TemplateEngine `os` and `host` constraints unchanged and SHALL identify itself to `host` as `func` with the CLI version. The CLI SHALL NOT register the .NET SDK `sdk-version` and `workload` constraint factories.

#### Scenario: Template requires a minimum CLI version
- **WHEN** a template declares a `host` constraint for `func` with a version range
- **THEN** the CLI version decides the result

#### Scenario: Template requires a .NET SDK version
- **WHEN** a template declares an `sdk-version` constraint
- **THEN** its evaluation is `NotEvaluated` and the template is blocked

### Requirement: Evaluation results
A template's evaluation SHALL be `Eligible` when every constraint is allowed, `Restricted` when a constraint is restricted, `NotEvaluated` when a declared type has no registered factory, and `Failed` when a constraint fails to initialize, receives invalid arguments, or throws. Type names SHALL match case-sensitively. A template with several unmet constraints SHALL take the first applicable state of `Failed`, `NotEvaluated`, and `Restricted`, and SHALL keep a diagnostic for every unmet constraint. Every state except `Eligible` SHALL block selection and invocation, including when destructive file replacement is allowed.

#### Scenario: Constraint type is unknown
- **WHEN** a template declares a constraint type this CLI doesn't register
- **THEN** the template's evaluation is `NotEvaluated`

#### Scenario: Constraint type differs only by case
- **WHEN** a template declares `Func-Workload`
- **THEN** the template's evaluation is `NotEvaluated`

#### Scenario: Constraint factory fails to initialize
- **WHEN** a registered constraint factory fails while the session starts
- **THEN** templates that declare its type evaluate as `Failed`

#### Scenario: Constraint throws
- **WHEN** a constraint fails while evaluating
- **THEN** the template's evaluation is `Failed`

#### Scenario: Several constraints are unmet
- **WHEN** a template has a missing workload and an unknown constraint type
- **THEN** its evaluation is `NotEvaluated`
- **AND** diagnostics describe both constraints

#### Scenario: Force is requested
- **WHEN** a command allows destructive file replacement for a template that isn't eligible
- **THEN** the template is still blocked

### Requirement: Restriction guidance
Every unmet constraint SHALL have a message that states the reason and, where one exists, a next step. Func constraints SHALL build theirs from the parsed requirement, and the only template text a next step contains SHALL be a validated workload name. An outdated extension bundles workload SHALL get fixed guidance rather than an update command. `host` restrictions SHALL have no next step. `NotEvaluated` results SHALL suggest updating the CLI when the type starts with `func-`, and `Failed` results SHALL suggest checking the template's constraint configuration, replacing any engine call to action. When a constraint lists alternatives and none is satisfied, the message SHALL give each alternative's reason and SHALL include a next step only when the arguments name a single workload. Commands SHALL render constraint messages as plain text with control characters removed.

#### Scenario: Restriction has a fix
- **WHEN** a required workload is missing
- **THEN** the diagnostic includes the install command for that workload

#### Scenario: Restriction has no fix
- **WHEN** the resolved bundle has another identity
- **THEN** the diagnostic explains the requirement without a next step

#### Scenario: Extension bundles workload is outdated
- **WHEN** a `func-workload` constraint on `bundles` finds only older versions
- **THEN** the next step explains how to install a newer extension bundle without an update command

#### Scenario: Func constraint type is unknown
- **WHEN** a template's evaluation is `NotEvaluated` for a type that starts with `func-`
- **THEN** the next step is to update the Azure Functions CLI

#### Scenario: Constraint is invalid
- **WHEN** a template's evaluation is `Failed`
- **THEN** the next step is to check the template's constraint configuration

#### Scenario: No alternative is satisfied
- **WHEN** a constraint lists two workloads and neither is loaded
- **THEN** the message gives the reason for each workload
- **AND** the diagnostic has no next step

#### Scenario: Message contains markup or control characters
- **WHEN** a constraint message contains console markup or control characters
- **THEN** the command displays the markup literally and omits the control characters

### Requirement: Restriction summaries
Every template that isn't eligible SHALL have a short summary, derived from its state and first unmet constraint, for display beside the template in pickers.

#### Scenario: Template needs a workload
- **WHEN** a template is restricted by a `func-workload` constraint
- **THEN** its summary is `requires workloads`

#### Scenario: Template uses an unsupported constraint
- **WHEN** a template's evaluation is `NotEvaluated`
- **THEN** its summary is `uses an unsupported constraint`

### Requirement: Azure-Samples workload declarations
Azure-Samples project templates SHALL declare `func-workload` constraints that require every stack their configuration actions declare. A synthesized template SHALL receive one constraint per distinct declared stack, named by the canonical stack and without a version range. Packaging SHALL reject constraint entries that aren't objects with a `type`, repeated constraint labels, invalid func arguments, stacks no constraint requires, and `func-bundle` constraints. Packaging SHALL NOT evaluate constraints against installed workloads.

#### Scenario: Synthesized template uses mixed stacks
- **WHEN** a descriptor declares Node and Python projects
- **THEN** the synthesized template requires the `node` and `python` workloads in separate constraints

#### Scenario: Authored template omits a stack workload
- **WHEN** an authored template's configuration actions declare a stack that no workload constraint requires
- **THEN** packaging fails

#### Scenario: Constraint label repeats
- **WHEN** an authored template repeats a constraint label
- **THEN** packaging fails

#### Scenario: Project template requires a resolved bundle
- **WHEN** an authored project template declares `func-bundle`
- **THEN** packaging fails
