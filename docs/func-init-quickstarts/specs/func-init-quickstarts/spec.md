## Purpose

Defines how `func init` discovers and runs installed quickstart project templates, explains workload restrictions, and configures one or more generated Functions projects.

## ADDED Requirements

### Requirement: Quickstarts are ordinary installed project templates

`func init` SHALL treat an installed quickstart as a TemplateEngine template whose `tags.type` value is `project`. It SHALL NOT require a quickstart-specific template type, package type, or invocation command. A project template MAY generate one or more independently configured Functions projects.

#### Scenario: Installed quickstart is invoked

- **WHEN** an installed quickstart project template is selected
- **THEN** `func init` invokes it through the same TemplateEngine project-template path as any other installed project template

#### Scenario: Multi-project quickstart is installed

- **WHEN** one project template declares multiple Functions project configurations
- **THEN** `func init` treats the template as one selectable project template
- **AND** does not require `tags.type = solution`

#### Scenario: Item template is supplied

- **WHEN** `--template` identifies an item template
- **THEN** `func init` rejects it and directs the user to `func new`

### Requirement: Template packages are installed explicitly

`func init` SHALL resolve templates only from the installed func TemplateEngine catalog. It MUST NOT install a template package implicitly. When a requested template is not installed, the command SHALL fail without modifying template package state and SHALL provide the browse URL and explicit `func new install` guidance.

#### Scenario: Requested template is installed

- **WHEN** `--template` matches an installed project template
- **THEN** initialization continues with that installed template

#### Scenario: Requested template is not installed

- **WHEN** `--template` matches no installed project template
- **THEN** initialization exits non-zero
- **AND** directs the user to browse packages and install one explicitly

#### Scenario: Requested template is trusted first-party content

- **WHEN** an uninstalled template could be identified as first-party
- **THEN** the initial capability still does not install it implicitly

### Requirement: Interactive initialization is template-first

When no template is supplied and prompting is available, `func init` SHALL present installed project-template groups before resolving template variants, template parameters, stack filters, or language filters. After a template group is selected, the command SHALL resolve only the remaining choices required by that group.

#### Scenario: Multiple installed templates are available

- **WHEN** an interactive user runs `func init` without `--template`
- **THEN** the first creation choice is an installed project-template group

#### Scenario: Selected template has additional choices

- **WHEN** the selected template group requires a variant or parameter value
- **THEN** the command resolves or prompts for those choices after template selection

#### Scenario: One installed project template is available

- **WHEN** exactly one eligible installed project-template group is available
- **THEN** the command may select it automatically

### Requirement: Explicit template references remain authoritative

`--template` SHALL match exact full template identities before exact short names, case-insensitively where supported by the shared template integration. An explicit reference SHALL NOT fall back to another template when it is unknown, restricted, incompatible with explicit filters, or invalid.

#### Scenario: Full identity is supplied

- **WHEN** `--template` exactly matches a full installed project-template identity
- **THEN** that identity is selected before short-name matching

#### Scenario: Short name is ambiguous

- **WHEN** an explicit short name matches multiple installed project-template groups
- **THEN** initialization fails or prompts according to the shared ambiguity policy
- **AND** does not select an arbitrary group

### Requirement: The installed-template experience includes browse guidance

The interactive installed-template experience SHALL show a stable Functions-owned browse URL that users can follow to discover available template packages and installation instructions. The URL destination and backing catalog MAY evolve without changing the CLI command contract.

#### Scenario: Interactive template selection is displayed

- **WHEN** `func init` presents installed templates
- **THEN** it also displays the Functions-owned browse URL

#### Scenario: No project templates are installed

- **WHEN** the installed catalog contains no project templates
- **THEN** the command displays the browse URL and explicit installation guidance

### Requirement: Restricted installed templates remain visible

Installed project templates rejected by workload constraints SHALL remain visible in interactive template discovery as unavailable choices. The picker SHALL show a concise restriction summary, SHALL prevent selection of an unavailable template, and SHALL render detailed calls to action supplied by the constraint system outside the picker.

#### Scenario: Template is missing a required workload

- **WHEN** an installed template is restricted because a workload is missing
- **THEN** the template remains visible as unavailable
- **AND** the command displays the constraint system's acquisition guidance

#### Scenario: Template has an incompatible workload

- **WHEN** an installed template is restricted by an installed workload version or other workload incompatibility
- **THEN** the command displays the corresponding remediation guidance

#### Scenario: Explicitly requested template is restricted

- **WHEN** `--template` identifies an installed but restricted template
- **THEN** initialization exits non-zero before scaffolding
- **AND** displays its detailed calls to action

### Requirement: Constraint remediation is owned by the constraint system

`func init` SHALL consume structured eligibility, restriction summary, and customer call-to-action information from the template constraint system. This capability SHALL NOT define workload constraint declaration syntax, version semantics, package resolution, or remediation command construction.

#### Scenario: Constraint provides a call to action

- **WHEN** a template constraint reports a customer-remediable restriction
- **THEN** `func init` renders the supplied call to action without reconstructing constraint semantics

#### Scenario: Constraint cannot provide remediation

- **WHEN** a restriction has no automatic call to action
- **THEN** `func init` reports the restriction diagnostic and does not scaffold

### Requirement: Workload constraints are mandatory before scaffolding

Every selected project template SHALL satisfy its required workload constraints before `func init` modifies the target directory. Workload constraints MAY represent stack, host, bundle, or other workload requirements. Project initialization SHALL NOT require an existing-project bundle constraint.

#### Scenario: All workload requirements are satisfied

- **WHEN** every workload constraint for the selected template is eligible
- **THEN** initialization may proceed to preflight and scaffolding

#### Scenario: Bundle capability is supplied as a workload

- **WHEN** a project template requires bundle capability through a workload constraint
- **THEN** the requirement is evaluated as workload availability
- **AND** not as a resolved bundle constraint from a project that does not yet exist

### Requirement: Project configuration uses a required finalization action

Each Functions project generated by a project template SHALL have one required Functions project configuration action. The action SHALL reference one resolved primary-output file located directly in that Functions project root and SHALL supply canonical stack and language. The action SHALL generate `.func/config.json` through the CLI-owned configuration serializer.

#### Scenario: Single-project template declares configuration

- **WHEN** a template generates one Functions project
- **THEN** it declares one configuration action targeting a primary output in that project root

#### Scenario: Multi-project template declares configuration

- **WHEN** a template generates multiple Functions projects
- **THEN** it declares one configuration action for each project
- **AND** each action may declare a different canonical stack and language

#### Scenario: Action targets a renamed output

- **WHEN** TemplateEngine renames or relocates the referenced primary output
- **THEN** configuration is written relative to the resolved output path

### Requirement: Configuration action declarations are preflighted

Before target modification, `func init` SHALL validate every active Functions project configuration action. It SHALL require a supported trusted action, a unique resolved primary-output reference, a non-empty canonical stack and language, a project root within the target, and no duplicate project root. A template with no active Functions project configuration action SHALL be invalid for `func init`.

#### Scenario: No configuration action is declared

- **WHEN** a selected project template has no active Functions project configuration action
- **THEN** initialization fails before scaffolding with a template-authoring diagnostic

#### Scenario: Two actions target one project root

- **WHEN** multiple active configuration actions resolve to files with the same parent directory
- **THEN** initialization fails before scaffolding

#### Scenario: Action resolves outside the target

- **WHEN** a configuration action's primary output would resolve outside the initialization target
- **THEN** initialization fails before scaffolding

#### Scenario: Action uses unsupported behavior

- **WHEN** a template attempts to substitute another action for the trusted configuration action
- **THEN** the template is not invocable

### Requirement: Template content cannot own CLI project configuration

Project-template file effects MUST NOT create or modify `.func/config.json` directly. The required Functions project configuration action SHALL be the only project-template mechanism that generates the file.

#### Scenario: Template content includes project configuration

- **WHEN** project-template effects target a `.func/config.json` path
- **THEN** preflight fails before target modification

#### Scenario: Configuration action plans project configuration

- **WHEN** a valid configuration action targets a Functions project
- **THEN** its CLI-owned configuration effect is accepted

### Requirement: Stack and language filters apply to the whole template

Explicit `--stack` and `--language` values SHALL filter a selected template against every active Functions project configuration action. A supplied stack or language matches only when every generated Functions project declares that canonical value. A mixed-stack or mixed-language template remains available when the corresponding singular filter is absent.

#### Scenario: Every project matches the stack filter

- **WHEN** every active project configuration declares the requested stack
- **THEN** the template satisfies `--stack`

#### Scenario: One project conflicts with the stack filter

- **WHEN** any active project configuration declares another stack
- **THEN** the explicit stack filter rejects the template
- **AND** no project topology is rewritten

#### Scenario: Mixed-language template has no language filter

- **WHEN** a template declares multiple project languages
- **AND** the user does not supply `--language`
- **THEN** the mixed-language topology remains valid

#### Scenario: One project conflicts with the language filter

- **WHEN** any active project configuration declares another language than `--language`
- **THEN** initialization reports the whole-template conflict before scaffolding

### Requirement: Mixed project templates do not require singular language metadata

A project template that declares multiple Functions project configurations SHALL NOT be required to represent its complete topology through one TemplateEngine `tags.language` value. Configuration actions SHALL be authoritative for per-project stack and language. Standard language tags MAY continue to distinguish variants where one value accurately describes the complete template.

#### Scenario: Mixed-language template omits a singular language tag

- **WHEN** valid configuration actions declare multiple languages
- **THEN** the template remains eligible for `func init`

#### Scenario: Homogeneous variant declares a language tag

- **WHEN** a template variant's projects all use one language
- **THEN** its standard language tag may participate in variant selection

### Requirement: Mixed topology has no fabricated singular context

When active configuration actions declare multiple stacks or languages, `func init` MUST NOT expose an arbitrary stack or language as the singular func project context. Context consumers requiring one stack or language SHALL observe that value as unavailable. Homogeneous templates MAY expose their common canonical value.

#### Scenario: Template has one common stack and language

- **WHEN** every active project configuration declares the same stack and language
- **THEN** the prospective template context may expose those common values

#### Scenario: Template has mixed stacks

- **WHEN** active project configurations declare different stacks
- **THEN** singular `func:stack` context is unavailable

### Requirement: Non-interactive quickstart selection is deterministic

When prompting is unavailable or `--non-interactive` is supplied, `func init` SHALL require enough explicit input to identify one eligible project-template group, one compatible variant, and all required template parameter values. Restriction and whole-template filter failures SHALL remain errors rather than prompting or substituting another template.

#### Scenario: Template choice is ambiguous

- **WHEN** multiple eligible project-template groups remain non-interactively
- **THEN** the command lists explicit template references and requests `--template`

#### Scenario: Selected quickstart is complete

- **WHEN** one installed template, variant, parameter set, and active project topology are fully resolved
- **THEN** non-interactive initialization proceeds without prompts

### Requirement: Dry-run includes project configuration finalization

`func init --dry-run` SHALL resolve template parameters, active primary outputs, workload constraints, whole-template filters, template file effects, Functions project configuration actions, and ordinary post-actions without modifying the filesystem or executing actions. The preview SHALL show each planned `.func/config.json` at the project root derived from its resolved primary output.

#### Scenario: Multi-project quickstart is previewed

- **WHEN** a selected template declares multiple active configuration actions
- **THEN** dry-run shows one planned `.func/config.json` effect for each resolved project root

#### Scenario: Conditional project is absent

- **WHEN** template parameter evaluation disables a project and its configuration action
- **THEN** dry-run does not show configuration for that project

#### Scenario: Template has ordinary post-actions

- **WHEN** dry-run resolves ordinary post-actions
- **THEN** it reports them without execution

### Requirement: Configuration finalization precedes ordinary post-actions

After successful template scaffolding, `func init` SHALL execute required Functions project configuration actions before any ordinary template post-action. Each configuration write SHALL use the current CLI schema and SHALL be atomic at the file level. Ordinary post-actions SHALL run only after all project configurations succeed.

#### Scenario: Every project configuration succeeds

- **WHEN** template scaffolding and all configuration actions complete
- **THEN** each project contains CLI-owned `.func/config.json`
- **AND** ordinary post-actions may execute

#### Scenario: Configuration action fails

- **WHEN** a configuration action fails after scaffolding
- **THEN** initialization exits non-zero
- **AND** ordinary post-actions are not executed

#### Scenario: Configuration action is marked best-effort

- **WHEN** a template attempts to make the required configuration action optional or continue on error
- **THEN** preflight rejects the action declaration

### Requirement: Post-scaffolding configuration failure is partial initialization

`func init` SHALL NOT delete generated template content when required configuration finalization fails after scaffolding. It SHALL report partial initialization, identify the failed project configuration, and preserve any files and configurations already written.

#### Scenario: First configuration write fails

- **WHEN** scaffolding succeeded but the first project configuration cannot be written
- **THEN** generated content remains in place
- **AND** the command reports partial initialization

#### Scenario: Later configuration write fails

- **WHEN** one project was configured before another configuration fails
- **THEN** the successful configuration and generated content remain
- **AND** the command reports which project failed

### Requirement: Force behavior remains an ordered command effect

`--force` SHALL clear target content except `.git` only after template, parameter, constraint, filter, primary-output, and configuration-action preflight succeeds. It SHALL NOT bypass unavailable templates, workload restrictions, configuration requirements, or whole-template filters.

#### Scenario: Restricted template is forced

- **WHEN** a selected template is workload-restricted and `--force` is supplied
- **THEN** initialization fails before cleanup

#### Scenario: Valid forced initialization proceeds

- **WHEN** all selection and preflight checks succeed
- **THEN** non-git target content is cleared before scaffolding and configuration

### Requirement: Existing-project adoption remains metadata-only

Existing-project adoption and healing SHALL continue without project-template discovery, quickstart selection, template invocation, or configuration post-actions. Supplying `--template` on an adoption or healing path without `--force` SHALL remain an error. This capability SHALL NOT add recursive multi-project adoption.

#### Scenario: Existing root project is adoptable

- **WHEN** the target is an adoptable existing Functions project and `--force` is absent
- **THEN** `func init` uses the metadata-only adoption path

#### Scenario: Existing solution contains nested projects

- **WHEN** the target contains nested Functions projects but is not itself an adoptable root project
- **THEN** this capability does not recursively adopt those projects

### Requirement: Quickstart UX uses standard interaction and result boundaries

Installed-template lists, unavailable annotations, browse guidance, calls to action, dry-run effects, partial initialization, and success output SHALL use the standard CLI interaction boundary and func-owned result models. Constraint and post-action implementation types MUST NOT leak into command rendering.

#### Scenario: Restricted templates are rendered

- **WHEN** interactive discovery includes restricted entries
- **THEN** output uses standard themed CLI rendering without raw constraint implementation details

#### Scenario: Multi-project initialization succeeds

- **WHEN** all generated projects are configured and ordinary post-actions complete
- **THEN** success output identifies the selected template and configured project roots
