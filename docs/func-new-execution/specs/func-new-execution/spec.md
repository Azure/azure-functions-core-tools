## Purpose

Defines how `func new` resolves an existing Functions project, selects and configures an eligible item template, and either previews or performs its invocation.

## ADDED Requirements

### Requirement: Item template command grammar
`func new` SHALL accept a template reference as its primary positional argument and SHALL NOT interpret that argument as a filesystem path. The command SHALL accept `--path` as the directory in which the item template runs and SHALL accept `--template` / `-t` as an alternative explicit template selector. Supplying both selectors SHALL be an error.

#### Scenario: Positional template reference
- **WHEN** a user runs `func new timer`
- **THEN** the command interprets `timer` as the template reference

#### Scenario: Explicit template reference
- **WHEN** a user supplies `--template Contoso.Functions.Timer`
- **THEN** the command resolves that value as the template reference

#### Scenario: Template execution path is explicit
- **WHEN** a user supplies `--path ./src/MyFunctions/functions`
- **THEN** the command runs the selected template in that directory rather than treating it as the template reference

#### Scenario: Both selectors are supplied
- **WHEN** a user supplies both positional `<template>` and `--template`
- **THEN** the command rejects the invocation and explains that only one template selector can be used

### Requirement: Item-only template scope
`func new` SHALL discover, list, resolve, and invoke only templates whose TemplateEngine `tags.type` value identifies them as item templates. Exact identity selection SHALL NOT bypass the item-template requirement.

#### Scenario: Short name is shared across template types
- **WHEN** project and item templates share a short name
- **THEN** `func new` considers only the item-template candidates when resolving that short name

#### Scenario: Explicit project template identity
- **WHEN** `--template` identifies a project template
- **THEN** the command refuses invocation and directs the user to `func init`

#### Scenario: Item templates are listed
- **WHEN** a user requests the `func new` template list
- **THEN** the ordinary list contains item templates and excludes project templates

### Requirement: Existing project context
`func new` SHALL discover the containing Functions project by walking the directory hierarchy upward from the template execution path. It SHALL resolve that project's stack, language when available, extension bundle identity, and extension bundle version before creating its command-scoped template environment. Template discovery, constraints, bindings, parsing, and invocation SHALL use that immutable execution-path and project-context snapshot.

#### Scenario: Execution path is inside an initialized project
- **WHEN** the template execution path is the project root or one of its descendants
- **THEN** the command discovers and uses that containing Functions project

#### Scenario: Execution path is outside a Functions project
- **WHEN** walking upward from the template execution path finds no Functions project
- **THEN** the command exits without template invocation and directs the user to run `func init`

#### Scenario: Execution path does not yet exist
- **WHEN** `--path` identifies a directory that does not yet exist beneath a Functions project
- **THEN** project discovery walks upward from its nearest existing ancestor and template invocation uses the requested directory

#### Scenario: Execution path and project root differ
- **WHEN** `--path` identifies a nested directory beneath the project root
- **THEN** the template working directory is the nested path while project compatibility context comes from the discovered project root

### Requirement: Strict staged argument parsing
`func new` SHALL first parse its stable command arguments while preserving the original template-specific token sequence. It SHALL then parse that same sequence independently against each remaining template candidate's projected parameter contract. The candidate-specific parse SHALL reject unknown aliases, missing option values, invalid types, and invalid choices.

#### Scenario: Candidate-specific option is valid
- **WHEN** a supplied template option is valid for one candidate
- **THEN** the command retains that candidate and maps the value to its canonical template symbol

#### Scenario: Option is unknown to every candidate
- **WHEN** no remaining candidate recognizes a supplied template option
- **THEN** the command reports the unknown option and does not invoke a template

#### Scenario: Candidate expects a different value type
- **WHEN** a template option value cannot be converted to a candidate's declared type
- **THEN** that candidate does not survive argument filtering and its diagnostic is preserved

#### Scenario: Invalid arguments eliminate every candidate
- **WHEN** candidate-specific parsing leaves no matching candidate
- **THEN** the command reports relevant item-specific diagnostics rather than silently ignoring the arguments

### Requirement: Reserved command arguments
Stable `func new` arguments, help aliases, inherited command aliases, and template-package lifecycle names SHALL be reserved from direct template-option assignment. A conflicting template parameter SHALL remain addressable through an unambiguous fallback alias. The `install`, `update`, and `uninstall` template references SHALL be addressable through `--template` / `-t`.

#### Scenario: Template parameter long name collides
- **WHEN** a projected template parameter long name collides with a reserved command argument
- **THEN** help and parsing expose that parameter through its fallback `--param:<canonical-name>` alias

#### Scenario: Template parameter short name collides
- **WHEN** a projected template parameter short name collides with a reserved command argument
- **THEN** help and parsing expose that parameter through an unambiguous fallback short alias

#### Scenario: Template name matches lifecycle subcommand
- **WHEN** a user intends to invoke an item template named `install`
- **THEN** the user can select it through `func new --template install`

#### Scenario: Reserved name is used positionally
- **WHEN** the token after `func new` names a registered lifecycle subcommand
- **THEN** the command routes to that subcommand rather than interpreting the token as an item template

### Requirement: Deterministic candidate narrowing
`func new` SHALL apply explicit template identity, item type, mandatory constraints, explicit language, project language when no explicit language was supplied, and candidate argument compatibility before applying template precedence. Precedence SHALL be an explicit final narrowing step and SHALL NOT discard a candidate before those filters have been evaluated.

#### Scenario: Explicit language is supplied
- **WHEN** a user supplies `--language`
- **THEN** the command filters candidates using that language instead of preferring the project's ambient language

#### Scenario: Project language is used as fallback
- **WHEN** no explicit language is supplied and the project has a resolved language
- **THEN** the command filters candidates using the project language

#### Scenario: Arguments identify a lower-precedence candidate
- **WHEN** supplied arguments are valid only for a lower-precedence candidate
- **THEN** argument filtering retains that candidate before precedence is considered

#### Scenario: Multiple precedence levels remain
- **WHEN** explicit filtering and argument compatibility leave candidates at different precedence levels
- **THEN** the command retains only candidates at the highest remaining precedence

### Requirement: Prompt only for unresolved decisions
Interactive `func new` execution SHALL prompt only when no template reference was supplied, multiple candidates remain after all deterministic narrowing, or the selected template requires a visible value that has no supplied or resolved default. It SHALL NOT prompt for optional parameters solely to collect additional customization.

#### Scenario: No template reference is supplied
- **WHEN** an interactive user runs `func new` without a template selector
- **THEN** the command prompts the user to select from eligible item templates

#### Scenario: Multiple candidates remain
- **WHEN** deterministic narrowing and precedence leave multiple candidates
- **THEN** the command prompts an interactive user to select one candidate

#### Scenario: Required visible parameter is unresolved
- **WHEN** the selected template has a visible required parameter without a supplied or resolved value
- **THEN** the command prompts for that parameter using its projected type and choices

#### Scenario: Optional parameters remain unset
- **WHEN** the selected template has optional parameters without supplied values
- **THEN** the command proceeds without prompting for those parameters

#### Scenario: Explicit value is invalid
- **WHEN** a user supplied a value that fails validation
- **THEN** the command reports the error rather than prompting for a replacement value

### Requirement: Non-interactive execution
`func new` SHALL provide `--non-interactive`. When that switch is supplied, or when the terminal cannot support interaction, any state that requires a prompt SHALL fail with actionable diagnostics and no template invocation.

#### Scenario: Template selection would require a prompt
- **WHEN** multiple templates remain and `--non-interactive` is supplied
- **THEN** the command reports the remaining candidates and explains how to select one explicitly

#### Scenario: Required value would require a prompt
- **WHEN** a required template parameter is unresolved and `--non-interactive` is supplied
- **THEN** the command reports every unresolved required parameter and its effective command alias

#### Scenario: Terminal is not interactive
- **WHEN** the interaction service cannot prompt and required input remains unresolved
- **THEN** the command fails as a non-interactive invocation even if the switch was omitted

### Requirement: Canonical template invocation
After one candidate is selected and all required values are resolved, `func new` SHALL invoke that exact resolved item template using values keyed by canonical template symbol name. `--force` SHALL control file-conflict behavior only and SHALL NOT bypass template type, constraints, parsing, or validation.

#### Scenario: Template is ready to invoke
- **WHEN** one eligible candidate remains and all required values are resolved
- **THEN** the command invokes that candidate with its canonical symbol-value mapping

#### Scenario: Output would overwrite a file
- **WHEN** actual invocation detects a destructive file change and `--force` was not supplied
- **THEN** the command refuses the destructive change and explains how to opt in

#### Scenario: Force is supplied
- **WHEN** `--force` is supplied for an otherwise valid invocation
- **THEN** invocation permits conflicting file changes without bypassing any compatibility or argument checks

### Requirement: Template execution path
`func new` SHALL use `--path` as both the TemplateEngine working directory and the directory where item-template output is applied. A relative path SHALL be resolved from the process current directory, and an omitted path SHALL default to the process current directory. The containing Functions project root SHALL be discovered separately and SHALL NOT replace the template execution path.

#### Scenario: Path is omitted
- **WHEN** a user does not supply `--path`
- **THEN** the command runs the template in the process current directory and discovers project context by walking upward from that directory

#### Scenario: Relative path is supplied
- **WHEN** a user supplies a relative `--path`
- **THEN** the command resolves it relative to the process current directory and runs the template there

#### Scenario: Nested path is supplied
- **WHEN** a user supplies a path below the discovered project root
- **THEN** generated files are applied at that nested path rather than at the project root

### Requirement: Dry-run preview
`func new` SHALL provide `--dry-run` and use TemplateEngine's dry-run operation to evaluate the selected template without applying filesystem changes or executing post-actions. The command SHALL render the file changes and post-actions that actual invocation would produce.

#### Scenario: Dry-run succeeds
- **WHEN** a user invokes an eligible template with `--dry-run`
- **THEN** the command reports the projected creation effects and leaves the filesystem unchanged

#### Scenario: Dry-run detects destructive changes
- **WHEN** previewed creation effects include file modifications or deletions
- **THEN** the command identifies those effects and whether actual execution would require `--force`

#### Scenario: Template defines post-actions
- **WHEN** a dry-run template result includes post-actions
- **THEN** the command reports the post-actions without executing them

#### Scenario: Dry-run requires missing input
- **WHEN** template selection or required parameter resolution would require a prompt
- **THEN** dry-run follows the same interactive or non-interactive behavior as actual invocation
