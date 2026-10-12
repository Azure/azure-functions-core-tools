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

### Requirement: Consumer-side raw declaration validation
The CLI SHALL validate every template's raw `.template.config/template.json` constraint declarations during isolated package install/update preflight and installed-template listing and resolution, including third-party packages and folder installs. Validation SHALL reject a present non-object constraints section, non-object entries, missing, non-string, or empty types, invalid func arguments, a repeated top-level `constraints` member, and duplicate property names in any object within the constraints subtree before normalization can discard them. Duplicate checks SHALL be local to each object. An omitted or empty constraints object SHALL be valid. Validation SHALL NOT depend on installed workloads or reject a well-formed declaration solely because its type is unknown. Listing and resolution SHALL classify malformed or unreadable declarations as `Failed` and SHALL evaluate constraints from the same validated content snapshot. Cached engine metadata SHALL NOT replace raw validation. Invocation SHALL reject selected-template configuration that is unreadable or changed since that snapshot, without resolving the reference again or substituting a template.

#### Scenario: Third-party package has a malformed constraint
- **WHEN** an install or update stages a third-party template package with a non-object constraint entry
- **THEN** preflight rejects the package before live hive mutation
- **AND** any previous installation remains unchanged

#### Scenario: Raw declaration has a repeated label
- **WHEN** raw template configuration repeats a constraint label that engine normalization would replace
- **THEN** consumer validation rejects the declaration instead of evaluating only the replacement

#### Scenario: Top-level constraints member repeats
- **WHEN** raw template configuration contains a requirement in one `constraints` member followed by another `constraints` member
- **THEN** validation rejects the configuration before the later member can hide the earlier requirement

#### Scenario: Constraint or argument properties repeat
- **WHEN** a constraint object repeats `type` or `args`, or an object-form argument repeats `id` or `version`
- **THEN** validation rejects the configuration before either value can replace the other

#### Scenario: Separate alternatives have the same property names
- **WHEN** separate alternative argument objects each declare one `id` and one `version`
- **THEN** duplicate-property validation accepts the distinct objects
- **AND** the alternatives remain subject to their normal argument and eligibility checks

#### Scenario: Constraint type is not a string
- **WHEN** a raw constraint entry has a numeric, boolean, null, object, or array type value
- **THEN** consumer validation rejects it instead of coercing it into an unknown type

#### Scenario: Installed template has malformed declarations
- **WHEN** listing or resolution reads malformed raw declarations from an already-installed template
- **THEN** the template is reported as `Failed` with an authoring diagnostic
- **AND** it cannot become invocation-ready even if the engine cache omits the malformed constraint

#### Scenario: Folder configuration changes after selection
- **WHEN** a selected folder template's configuration changes after listing or resolution validated it
- **THEN** invocation fails before creation with guidance to rerun
- **AND** the command does not resolve its reference again or substitute another template

#### Scenario: Template configuration becomes unreadable
- **WHEN** selected-template configuration cannot be read before invocation
- **THEN** invocation fails before creation instead of trusting cached declarations

#### Scenario: Unknown type is well formed
- **WHEN** a raw declaration has a valid unknown type and otherwise valid structure
- **THEN** raw validation preserves the declaration
- **AND** eligibility is `NotEvaluated` and blocks selection

#### Scenario: Template declares no constraints
- **WHEN** raw configuration omits constraints or declares an empty constraints object
- **THEN** raw validation succeeds
- **AND** eligibility remains subject to the template's other checks

### Requirement: Func constraint types stay in func template packages
Func constraint types SHALL use the `func-` prefix. A template package that other template hosts also install SHALL NOT declare func constraint types. First-party templates that require func constraint types SHALL ship in func-only packages. Packaging and isolated install/update preflight SHALL reject func constraint types when NuGet metadata also declares `Template` or another supported host-sharing declaration. Installed-template validation SHALL apply the same rule to available package metadata and classify violations as `Failed`. Folder installs without NuGet package-type metadata SHALL retain raw declaration validation without a synthesized shared-package classification.

#### Scenario: Package is also a dotnet new package
- **WHEN** a template package also declares the `Template` package type and a template declares a func constraint type
- **THEN** packaging fails

#### Scenario: Prebuilt shared package declares func constraints
- **WHEN** install or update stages a third-party NuGet package that declares `FuncTemplate` and `Template` and contains func constraint types
- **THEN** preflight rejects it before live mutation
- **AND** force cannot bypass the shared-package rule

#### Scenario: Already-installed shared package declares func constraints
- **WHEN** an installed NuGet package's available metadata declares `Template` and a template declares func constraint types
- **THEN** installed-template validation reports the template as `Failed` and blocks selection

#### Scenario: Shared package has no func constraint types
- **WHEN** a template package also declares the `Template` package type and its templates declare no func constraint types
- **THEN** the shared-package rule does not reject it

#### Scenario: Folder has no package-type metadata
- **WHEN** a folder template declares valid func constraints without NuGet package-type metadata
- **THEN** consumer validation does not invent a shared-package classification
- **AND** the template remains subject to raw declaration and eligibility checks

#### Scenario: Func-only package requires a stack
- **WHEN** a first-party func-only template package declares a `func-workload` requirement
- **THEN** the shared-package rule permits that declaration

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

### Requirement: Compatibility and eligibility boundaries
The workload subsystem SHALL own applicable CLI/workload contract, packaging, and activation checks. The template subsystem SHALL own template package classification and supported format and feature checks. The workload snapshot used by `func-workload` SHALL exclude workloads rejected by applicable compatibility or activation checks. Template constraints SHALL NOT replace or bypass those checks, select replacement package versions, or acquire packages.

#### Scenario: Installed workload was rejected before activation
- **WHEN** a matching workload is installed but rejected by compatibility checks and no matching usable workload is present
- **THEN** the workload cannot satisfy the template's requirement
- **AND** the template is restricted without a new install or update command
- **AND** the command retains the CLI-owned compatibility diagnostic instead of classifying the workload as missing

#### Scenario: Usable workload satisfies the requirement
- **WHEN** a matching workload passes applicable compatibility and activation checks and its version satisfies the template's requirement
- **THEN** the workload constraint is satisfied

#### Scenario: Workload passes compatibility but misses the template range
- **WHEN** a matching workload passes applicable compatibility and activation checks but its version is outside the template's range
- **THEN** package compatibility does not make the template eligible

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
Func constraint versions SHALL use NuGet version range syntax, including floating versions, where a bare version is a minimum and an omitted version accepts any version. A prerelease version SHALL also satisfy a range when its release version is in the range. An invalid range SHALL make the constraint fail. This prerelease rule SHALL apply only to `func-workload` and `func-bundle` eligibility and SHALL NOT alter package selection, CLI minimums, or managed-contract compatibility checks.

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

#### Scenario: Preview CLI is below a stable minimum
- **WHEN** a template declares a `host` constraint for `func` with range `[5.2.0,)` and the CLI is `5.2.0-preview.1`
- **THEN** the template is restricted
- **AND** the func workload prerelease rule does not change the host result

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

#### Scenario: Compatible replacement is not known
- **WHEN** a constraint gives install or update guidance and no compatible replacement version is known
- **THEN** guidance does not name a supposedly compatible version or promise automatic compatible-version selection
- **AND** it does not change a version or profile pin

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
