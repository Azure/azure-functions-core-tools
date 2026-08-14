## Purpose

Defines how `func init` selects an installed stack, language, and project template before creating or adopting an Azure Functions project.

## ADDED Requirements

### Requirement: Init command inputs
`func init` SHALL retain its optional positional project path and SHALL accept `--stack`, `--language`, and `--template` as independent explicit filters. It SHALL also provide `--non-interactive` and `--dry-run`. The project template SHALL be selected only through `--template`; the positional argument SHALL remain the target project directory.

#### Scenario: Target path is supplied
- **WHEN** a user runs `func init ./apps/orders`
- **THEN** the command treats `./apps/orders` as the project directory rather than a template reference

#### Scenario: Template is supplied
- **WHEN** a user supplies `--template basic`
- **THEN** the command resolves `basic` as a project-template reference

#### Scenario: Multiple explicit filters are supplied
- **WHEN** a user supplies `--stack`, `--language`, and `--template`
- **THEN** the command validates the exact requested stack-language-template combination

### Requirement: Installed stack metadata
`func init` SHALL derive available stacks and languages exclusively from installed stack workloads. Each installed stack SHALL expose a canonical stack ID, display name, worker runtime aliases, canonical languages, and language aliases. Stack workloads SHALL NOT directly scaffold project files or contribute template-specific command options.

#### Scenario: Installed stacks are available
- **WHEN** multiple stack workloads are installed
- **THEN** stack and language selection uses their declared canonical metadata and aliases

#### Scenario: No stack workload is installed
- **WHEN** no installed stack metadata is available
- **THEN** the command does not create a project and directs the user to set up a stack workload

#### Scenario: Two workloads claim one stack
- **WHEN** multiple installed workloads declare the same canonical stack ID
- **THEN** initialization fails with a workload conflict diagnostic

#### Scenario: Workload-specific option is needed
- **WHEN** a project template requires a value previously supplied through a workload-contributed init option
- **THEN** that value is exposed and parsed as a project-template symbol

### Requirement: Project-template metadata
`func init` SHALL consider only templates whose TemplateEngine `tags.type` value identifies them as project templates. Every project-template variant SHALL declare a recognized canonical `language` tag. Templates with missing or unsupported language metadata SHALL be excluded with template-authoring diagnostics.

#### Scenario: Project and item templates share a short name
- **WHEN** project and item templates share a short name
- **THEN** `func init` considers only project-template candidates

#### Scenario: Explicit item-template identity
- **WHEN** `--template` identifies an item template
- **THEN** the command refuses invocation and directs the user to `func new`

#### Scenario: Project template omits language
- **WHEN** a project-template variant has no language tag
- **THEN** it is not eligible for initialization and its diagnostic identifies the missing required metadata

#### Scenario: Project template declares unsupported language
- **WHEN** no installed stack recognizes a project-template variant's language
- **THEN** that variant is not an applicable initialization candidate

### Requirement: Stack-template compatibility
`func init` SHALL derive compatible initialization candidates by intersecting each project-template variant's canonical language with the canonical languages supported by installed stacks. A language MAY be owned by multiple installed stacks, and every compatible owner SHALL remain available until explicitly selected or prompted.

#### Scenario: One stack owns the template language
- **WHEN** one installed stack supports a project-template variant's language
- **THEN** that stack-template-language combination is compatible

#### Scenario: Multiple stacks own the template language
- **WHEN** multiple installed stacks support the same project-template language
- **THEN** all matching stacks remain candidates and the command does not silently choose the first registered workload

#### Scenario: Stack has no template for one language
- **WHEN** an installed stack supports a language for which no project template is installed
- **THEN** that language is omitted from applicable initialization choices

### Requirement: Explicit filters are authoritative
`func init` SHALL apply every supplied stack, language, and template filter before prompting or automatically selecting any unresolved dimension. Canonical names and declared aliases SHALL match case-insensitively. A supplied value that matches no compatible candidate SHALL fail rather than falling back to another choice.

#### Scenario: Stack is supplied
- **WHEN** `--stack` identifies an installed stack
- **THEN** only that stack's applicable languages and project templates remain

#### Scenario: Language is supplied
- **WHEN** `--language` identifies a language owned by one installed stack
- **THEN** that stack and language are selected before project templates are considered

#### Scenario: Language has multiple stack owners
- **WHEN** `--language` matches multiple installed stacks
- **THEN** the command retains those stacks for explicit or interactive selection

#### Scenario: Stack and language conflict
- **WHEN** the requested stack does not support the requested language
- **THEN** the command reports the conflict and does not prompt for a substitute

#### Scenario: Template and stack conflict
- **WHEN** the requested project template has no language variant supported by the requested stack
- **THEN** the command reports the compatible stacks and languages without invoking another template

### Requirement: Progressive automatic and interactive selection
After explicit filtering, `func init` SHALL automatically select any dimension with exactly one remaining value. It SHALL prompt only for stack, language, or project-template choices that remain genuinely ambiguous. When no template was explicitly supplied, stack SHALL be resolved before language and language before template presentation.

#### Scenario: No filters are supplied
- **WHEN** multiple installed stacks, languages, and templates are applicable
- **THEN** the interactive command prompts for stack, then language, then project template

#### Scenario: Stack has one applicable language
- **WHEN** the selected stack has exactly one language with an installed project template
- **THEN** the command selects that language without prompting

#### Scenario: One applicable project template remains
- **WHEN** stack and language filtering leaves one project-template group
- **THEN** the command selects it without prompting regardless of its short name

#### Scenario: Multiple project templates remain
- **WHEN** stack and language filtering leaves multiple project-template groups
- **THEN** the command prompts an interactive user to select one

#### Scenario: Template is supplied first
- **WHEN** `--template` resolves a group supporting multiple installed stack-language combinations
- **THEN** the command prompts only for unresolved compatible stacks and languages

### Requirement: Non-interactive initialization
When `--non-interactive` is supplied, or the terminal cannot prompt, `func init` SHALL fail whenever more than one compatible stack, language, template, or required template value remains. Diagnostics SHALL identify every available explicit choice needed to complete the command.

#### Scenario: Stack choice is ambiguous
- **WHEN** multiple compatible stacks remain in non-interactive execution
- **THEN** the command lists their canonical IDs and requests `--stack`

#### Scenario: Language choice is ambiguous
- **WHEN** a selected stack has multiple applicable languages in non-interactive execution
- **THEN** the command lists their canonical labels and requests `--language`

#### Scenario: Template choice is ambiguous
- **WHEN** multiple project-template groups remain in non-interactive execution
- **THEN** the command lists their references and requests `--template`

#### Scenario: Every dimension is unique
- **WHEN** filtering leaves exactly one stack, language, template, and complete parameter set
- **THEN** non-interactive initialization proceeds without prompts

### Requirement: Prospective project context
Before context-dependent template resolution, `func init` SHALL create one immutable prospective project context containing the target directory, selected canonical stack, and selected canonical language. Template constraints, host bindings, parameter defaults, dry-run, and creation SHALL use that same context. Extension bundle identity and version SHALL remain unavailable because no project bundle has yet been generated or resolved.

#### Scenario: Project does not yet exist
- **WHEN** initialization targets an empty directory
- **THEN** project templates receive the target directory, selected stack, and selected language as project context

#### Scenario: Project template reads host bindings
- **WHEN** a project template binds to func stack or language host context
- **THEN** it receives the selected canonical values

#### Scenario: Project template requires resolved bundle context
- **WHEN** a project template declares a compatibility requirement for an existing resolved bundle
- **THEN** that requirement cannot be satisfied during new-project initialization

### Requirement: Strict project-template argument parsing
After selecting a prospective stack and language context, `func init` SHALL parse project-template arguments using the same strict candidate-specific contract as `func new`. Reserved aliases, collision fallbacks, invalid-input handling, missing-required-value prompting, canonical symbol mapping, and precedence ordering SHALL be consistent between the commands.

#### Scenario: Template-specific option is valid
- **WHEN** a supplied option is valid for one project-template candidate
- **THEN** the command retains that candidate and maps the value to its canonical symbol

#### Scenario: Template-specific option is invalid
- **WHEN** a supplied option is unknown or has an invalid value
- **THEN** the command reports the candidate-specific error rather than ignoring the option or prompting for a replacement

#### Scenario: Required symbol is unresolved
- **WHEN** the selected project template has a visible required symbol without a value or default
- **THEN** the interactive command prompts for that symbol and non-interactive execution reports it

#### Scenario: Argument filtering and precedence both apply
- **WHEN** explicit arguments distinguish candidates at different precedence levels
- **THEN** argument compatibility is applied before highest remaining precedence

### Requirement: Initialization state boundaries
`func init` SHALL invoke a project template only for an empty target or a target explicitly reinitialized with `--force`. Existing project adoption and partial-project healing SHALL continue without project-template selection or invocation.

#### Scenario: Existing project is adoptable
- **WHEN** the target contains an adoptable Functions project and `--force` is absent
- **THEN** the command writes or repairs CLI project metadata without running a project template

#### Scenario: Existing project needs language healing
- **WHEN** the target has CLI configuration requiring language completion
- **THEN** the command resolves and persists language without running a project template

#### Scenario: Template is supplied during adoption
- **WHEN** `--template` is supplied for an adoption or healing path without `--force`
- **THEN** the command rejects the unused template request and explains that `--force` is required to reinitialize

#### Scenario: Existing initialized project
- **WHEN** the target is already initialized and `--force` is absent
- **THEN** the command refuses to scaffold over the project

### Requirement: Force reinitialization
`--force` SHALL reinitialize by deleting all target content except the `.git` directory before project-template creation. It SHALL NOT bypass installed-stack validation, project-template type, language compatibility, constraints, strict parsing, selection ambiguity, primary-output resolution, or configuration-action preflight.

#### Scenario: Force is confirmed interactively
- **WHEN** an interactive user requests `--force` for a non-empty target and confirms the destructive operation
- **THEN** existing non-git content is removed before project-template creation

#### Scenario: Force is declined
- **WHEN** the user declines the destructive confirmation
- **THEN** initialization stops without modifying the target

#### Scenario: Force runs non-interactively
- **WHEN** `--force` is supplied non-interactively
- **THEN** the explicit switch authorizes cleanup without a prompt

### Requirement: Project configuration uses trusted finalization actions
Every Functions project generated by a project template SHALL be represented by one mandatory trusted project configuration action. The action SHALL reference a resolved primary-output file located directly in the project root and SHALL supply canonical stack and language compatible with the selected init candidate. Func SHALL derive the project root from the resolved output's parent and generate `.func/config.json` through the CLI-owned serializer.

#### Scenario: Project configuration action is declared
- **WHEN** a project template generates a Functions project
- **THEN** it declares a trusted configuration action targeting a primary output in that project root

#### Scenario: Action targets a renamed output
- **WHEN** TemplateEngine renames or relocates the referenced primary output
- **THEN** configuration is written relative to the resolved output path

#### Scenario: Single-language stack is selected
- **WHEN** the selected stack currently supports one language
- **THEN** the action still supplies and persists that canonical language explicitly

#### Scenario: Action conflicts with selected candidate
- **WHEN** an active configuration action declares a stack or language incompatible with the selected init candidate
- **THEN** initialization fails before target modification

### Requirement: Configuration action declarations are preflighted
Before target modification, `func init` SHALL validate every active project configuration action and its resolved primary output. It SHALL require supported trusted behavior, a unique in-target project root, non-empty canonical stack and language, and mandatory failure semantics. A template with no active project configuration action SHALL be invalid for `func init`.

#### Scenario: No configuration action is declared
- **WHEN** a selected project template has no active trusted project configuration action
- **THEN** initialization fails before scaffolding with a template-authoring diagnostic

#### Scenario: Action reference is invalid
- **WHEN** a configuration action references a missing, ambiguous, inactive, non-file, or out-of-target primary output
- **THEN** initialization fails before scaffolding

#### Scenario: Configuration action is optional
- **WHEN** a template marks the required action optional or continue-on-error
- **THEN** initialization fails before scaffolding

#### Scenario: Project template content generates CLI configuration
- **WHEN** project-template file effects create or modify `.func/config.json`
- **THEN** initialization fails before target modification because configuration content is CLI-owned

#### Scenario: Configuration output collides
- **WHEN** a planned configuration path collides with another template or configuration effect
- **THEN** initialization fails before target modification

### Requirement: Dry-run includes configuration finalization
`func init --dry-run` SHALL perform complete stack, language, template, argument, constraint, required-value, primary-output, and configuration-action resolution without modifying the filesystem or executing actions. The preview SHALL combine `--force` cleanup, project-template files, planned CLI-owned `.func/config.json` writes, and ordinary post-actions in execution order.

#### Scenario: Empty project is previewed
- **WHEN** a user runs `func init --dry-run` for an empty target
- **THEN** the preview includes project-template files and each planned `.func/config.json` without writing either

#### Scenario: Forced project is previewed
- **WHEN** a user combines `--force` and `--dry-run` for a non-empty target
- **THEN** the preview includes non-git deletions followed by project creation and configuration finalization effects

#### Scenario: Template defines ordinary post-actions
- **WHEN** the selected project template defines ordinary post-actions
- **THEN** the preview reports them without execution

#### Scenario: Item template is previewed
- **WHEN** an item template is invoked in dry-run mode
- **THEN** no project configuration finalization effect is added

### Requirement: Configuration finalization precedes ordinary post-actions
After project-template scaffolding succeeds, `func init` SHALL execute active project configuration actions in declared order before every ordinary template post-action. Each configuration write SHALL use the current CLI schema and SHALL be atomic at the file level. Ordinary post-actions SHALL run by default only after all project configurations succeed.

#### Scenario: Project initialization succeeds
- **WHEN** project scaffolding and every configuration action complete
- **THEN** each declared project contains CLI-owned `.func/config.json`
- **AND** ordinary post-actions run in declared order

#### Scenario: Configuration generation fails after project creation
- **WHEN** project files were created but a configuration action fails
- **THEN** initialization exits non-zero and reports partial initialization
- **AND** generated files and successful prior configurations remain
- **AND** ordinary post-actions are not run

#### Scenario: Dry-run has post-actions
- **WHEN** initialization is a dry-run
- **THEN** configuration and ordinary post-actions are reported but not executed

### Requirement: Missing applicable project templates
`func init` SHALL fail with actionable guidance when no installed project template supports the selected installed stack and language. The command SHALL NOT fall back to workload-owned scaffolding.

#### Scenario: Stack and language have no template
- **WHEN** an installed stack and language are selected but no compatible project template is installed
- **THEN** the command directs the user to install an applicable template package through `func new install`

#### Scenario: Former workload initializer exists
- **WHEN** legacy workload scaffolding code exists but no compatible project template is installed
- **THEN** the command does not invoke the legacy initializer as a fallback
