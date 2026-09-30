## Purpose

Defines how `func init` selects an installed project template and applies stack and language filters before creating or adopting an Azure Functions project.

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
- **THEN** the command validates that the requested template satisfies both filters

### Requirement: Installed stack metadata
`func init` SHALL derive available stacks and languages exclusively from installed stack workloads. Each installed stack SHALL expose a canonical stack ID, display name, worker runtime aliases, canonical languages, and language aliases. A canonical language MAY be supported by multiple installed stacks. Stack workloads SHALL NOT directly scaffold project files or contribute template-specific command options.

#### Scenario: Installed stacks are available
- **WHEN** multiple stack workloads are installed
- **THEN** stack and language filters use their declared canonical metadata and aliases

#### Scenario: Multiple stacks support one language
- **WHEN** multiple installed stacks declare the same canonical language
- **THEN** language lookup keeps every owning stack and does not silently choose the first registered workload

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
`func init` SHALL consider only templates whose TemplateEngine `tags.type` value identifies them as project templates. Each generated project's stack and language SHALL come from its configuration action. A singular `language` tag MAY distinguish homogeneous variants and SHALL NOT be required.

#### Scenario: Project and item templates share a short name
- **WHEN** project and item templates share a short name
- **THEN** `func init` considers only project-template candidates

#### Scenario: Explicit item-template identity
- **WHEN** `--template` identifies an item template
- **THEN** the command refuses invocation and directs the user to `func new`

#### Scenario: Mixed template omits a language tag
- **WHEN** a project template's configuration actions declare multiple languages and it has no language tag
- **THEN** it remains eligible for initialization

### Requirement: Explicit filters are authoritative
`func init` SHALL honor every supplied stack, language, and template filter and SHALL apply stack and language filters to the whole template, as `func-init-quickstarts` specifies. Canonical names and declared aliases SHALL match case-insensitively. A supplied value that matches no compatible candidate SHALL fail rather than falling back to another choice.

#### Scenario: Stack is supplied
- **WHEN** `--stack` identifies an installed stack
- **THEN** a project template is accepted only when every active project uses that stack

#### Scenario: Language is supplied
- **WHEN** `--language` identifies an installed language
- **THEN** a project template is accepted only when every active project uses that language

#### Scenario: Stack and language conflict
- **WHEN** the requested stack does not support the requested language
- **THEN** the command reports the conflict and does not prompt for a substitute

#### Scenario: Template and stack conflict
- **WHEN** the requested project template has an active project with another stack
- **THEN** the command reports the template's project stacks without invoking another template

### Requirement: Progressive automatic and interactive selection
`func init` SHALL automatically select any choice with exactly one remaining value. For group and variant choices, only eligible templates that can still match explicit stack and language filters remain. It SHALL prompt only for project-template group, variant, or required parameter choices that remain genuinely ambiguous. When no template was explicitly supplied, the project-template group SHALL be the first choice presented.

#### Scenario: No filters are supplied
- **WHEN** multiple installed project-template groups are applicable
- **THEN** the interactive command prompts for a project-template group first

#### Scenario: Group has one applicable variant
- **WHEN** the selected group has exactly one applicable variant
- **THEN** the command selects that variant without prompting

#### Scenario: One applicable project template remains
- **WHEN** exactly one applicable project-template group remains
- **THEN** the command selects it without prompting regardless of its short name

#### Scenario: Multiple project templates remain
- **WHEN** multiple applicable project-template groups remain
- **THEN** the command prompts an interactive user to select one

#### Scenario: Template is supplied first
- **WHEN** `--template` resolves a group with multiple applicable variants
- **THEN** the command prompts for the variant rather than another template

### Requirement: Non-interactive initialization
When `--non-interactive` is supplied, or the terminal cannot prompt, `func init` SHALL fail whenever more than one applicable template group or variant, or an unresolved required template value, remains. Diagnostics SHALL identify every available explicit choice needed to complete the command.

#### Scenario: Variant choice is ambiguous
- **WHEN** multiple variants of the selected group remain in non-interactive execution
- **THEN** the command lists their template identities and requests `--template`

#### Scenario: Template choice is ambiguous
- **WHEN** multiple project-template groups remain in non-interactive execution
- **THEN** the command lists their references and requests `--template`

#### Scenario: Every choice is resolved
- **WHEN** exactly one template, variant, and complete parameter set remain
- **THEN** non-interactive initialization proceeds without prompts

### Requirement: Init template context
Before listing project templates, `func init` SHALL create one immutable template context whose command directory and project root are the target directory. The context SHALL NOT expose a stack, language, or extension bundle. Listing, constraint evaluation, host bindings, parameter defaults, dry-run, and creation SHALL use one `Templater` created from that context.

#### Scenario: Project does not yet exist
- **WHEN** initialization targets an empty directory
- **THEN** project templates receive the target directory as project context

#### Scenario: Template reads host stack binding
- **WHEN** a project template binds `func:stack` or `func:language`
- **THEN** the host reports the value as unavailable, even when every project uses one stack and language
- **AND** the bind symbol receives its declared default

#### Scenario: Selected template is invoked
- **WHEN** the user selects a listed project template
- **THEN** init forms the group from the entries its `Templater` listed and invokes it through that `Templater` without looking the reference up again

#### Scenario: Project template requires resolved bundle context
- **WHEN** a project template declares a compatibility requirement for an existing resolved bundle
- **THEN** that requirement cannot be satisfied during new-project initialization

### Requirement: Strict project-template argument parsing
After selecting a project template, `func init` SHALL parse project-template arguments using the same strict candidate-specific contract as `func new`. Reserved aliases, collision fallbacks, invalid-input handling, missing-required-value prompting, canonical symbol mapping, and precedence ordering SHALL be consistent between the commands.

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
`--force` SHALL reinitialize by deleting all target content except the `.git` directory before project-template creation. It SHALL NOT bypass installed-stack validation, project-template type, whole-template filters, constraints, strict parsing, selection ambiguity, primary-output resolution, or configuration-action preflight.

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
Every Functions project generated by a project template SHALL be represented by one mandatory trusted project configuration action. The action SHALL reference a resolved primary-output file located directly in the project root and SHALL supply canonical stack and language that satisfy any explicit `--stack` and `--language` filters. Func SHALL derive the project root from the resolved output's parent and generate `.func/config.json` through the CLI-owned serializer.

#### Scenario: Project configuration action is declared
- **WHEN** a project template generates a Functions project
- **THEN** it declares a trusted configuration action targeting a primary output in that project root

#### Scenario: Action targets a renamed output
- **WHEN** TemplateEngine renames or relocates the referenced primary output
- **THEN** configuration is written relative to the resolved output path

#### Scenario: Project uses a single-language stack
- **WHEN** a generated project's stack currently supports one language
- **THEN** the action still supplies and persists that canonical language explicitly

#### Scenario: Action conflicts with an explicit filter
- **WHEN** an active configuration action declares a stack or language that conflicts with `--stack` or `--language`
- **THEN** initialization fails before target modification

### Requirement: Configuration action declarations are preflighted
Before target modification, `func init` SHALL validate every active project configuration action and its resolved primary output. It SHALL require supported trusted behavior, a unique in-target project root, non-empty canonical stack and language, an installed stack that supports that language, and mandatory failure semantics. A template with no active project configuration action SHALL be invalid for `func init`.

#### Scenario: No configuration action is declared
- **WHEN** a selected project template has no active trusted project configuration action
- **THEN** initialization fails before scaffolding with a template-authoring diagnostic

#### Scenario: Action reference is invalid
- **WHEN** a configuration action references a missing, ambiguous, inactive, non-file, or out-of-target primary output
- **THEN** initialization fails before scaffolding

#### Scenario: Action stack cannot support its language
- **WHEN** an active configuration action declares a stack that is not installed or a language that stack does not support
- **THEN** initialization fails before target modification, including with `--force`

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
`func init --dry-run` SHALL perform complete template, filter, argument, constraint, required-value, primary-output, and configuration-action resolution without modifying the filesystem or executing actions. The preview SHALL combine `--force` cleanup, project-template files, planned CLI-owned `.func/config.json` writes, and ordinary post-actions in execution order.

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
`func init` SHALL fail with actionable guidance when no installed project template satisfies the request. The command SHALL NOT fall back to workload-owned scaffolding.

#### Scenario: Filters match no installed template
- **WHEN** `--stack` or `--language` matches no installed project template
- **THEN** the command shows the browse URL and directs the user to install an applicable template package through `func new install`

#### Scenario: Former workload initializer exists
- **WHEN** legacy workload scaffolding code exists but no compatible project template is installed
- **THEN** the command does not invoke the legacy initializer as a fallback
