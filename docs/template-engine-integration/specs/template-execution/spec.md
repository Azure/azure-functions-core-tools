## Purpose

Defines the context-aware contract for discovering, evaluating, selecting, and invoking installed templates through the Azure Functions CLI template engine integration.

## ADDED Requirements

### Requirement: Command-scoped execution context
The integration SHALL operate from one immutable execution context containing the resolved command directory and any resolved project root, stack, language, extension bundle ID, and extension bundle version. The caller SHALL resolve this context before creating the integration, and the integration SHALL NOT read ambient process state as a substitute for a supplied value.

#### Scenario: Context is resolved before engine creation
- **WHEN** a command targets a directory other than the process current directory
- **THEN** template discovery, constraints, bindings, and invocation use the targeted command directory

#### Scenario: Context remains stable
- **WHEN** project or environment state changes after the integration has been created
- **THEN** all operations in that command invocation continue to use the original context snapshot

### Requirement: Func host context parameters
The integration SHALL make resolved context available to the template engine through the `WorkingDirectory`, `func:project-root`, `func:stack`, `func:language`, `func:bundle-id`, and `func:bundle-version` host parameters. The `func:` names form a func-owned namespace, and values SHALL preserve the canonical representations supplied by the context.

#### Scenario: Bundle constraint reads host context
- **WHEN** a template constraint requests `func:bundle-id` and `func:bundle-version`
- **THEN** it receives the exact resolved extension bundle identity and version for the command

#### Scenario: Working directory binding uses command context
- **WHEN** a template binds to `host:WorkingDirectory`
- **THEN** it receives the resolved command directory rather than the process current directory

#### Scenario: Optional context is unavailable
- **WHEN** a template requests a func host parameter that was not resolved for the command
- **THEN** the host reports that parameter as unavailable rather than synthesizing or reusing a value

### Requirement: Per-command engine environment
The integration SHALL create a fresh host and template engine environment for each command invocation and SHALL reuse that same environment for discovery, constraint evaluation, candidate metadata, selection, and invocation within the command. Persistent package registration and template cache data SHALL continue to use the shared func-owned settings location.

#### Scenario: Consecutive commands target different projects
- **WHEN** two commands in the same process resolve different project or bundle contexts
- **THEN** each command evaluates templates using only its own context

#### Scenario: One command evaluates multiple candidates
- **WHEN** a command discovers, filters, and invokes among multiple template candidates
- **THEN** every phase uses the same host defaults and engine environment

### Requirement: Installed template catalog
The integration SHALL list all templates known to the func settings hive, including templates that are ineligible in the current context. Each catalog entry SHALL include template identity, short names, group identity, language, precedence, owning package information when available, eligibility, and constraint diagnostics.

#### Scenario: Eligible and restricted templates are listed
- **WHEN** the installed catalog contains both eligible and context-restricted templates
- **THEN** listing returns both and identifies why each restricted template is ineligible

#### Scenario: Constraint cannot be evaluated
- **WHEN** an installed template declares a constraint that cannot be evaluated
- **THEN** listing distinguishes that authoring or configuration failure from an ordinary context restriction

### Requirement: Deterministic template reference matching
The integration SHALL first match a template reference against exact full template identities. If no full identity matches, it SHALL match exact short names case-insensitively. Full identity matching SHALL be the deterministic escape hatch from short-name ambiguity, but SHALL NOT bypass constraints.

#### Scenario: Full identity and short name both match
- **WHEN** the reference exactly matches one template identity and also matches another template's short name
- **THEN** only the exact identity match is considered

#### Scenario: Short name differs only by case
- **WHEN** a reference differs in case from an installed short name
- **THEN** the short name is considered a match

#### Scenario: No installed reference matches
- **WHEN** no installed template has the exact identity or short name
- **THEN** resolution returns a not-found outcome

### Requirement: Constraint-aware group resolution
The integration SHALL explicitly evaluate every matched candidate's constraints before making it eligible for selection. Candidates with restricted, failed, or not-evaluated constraints SHALL be ineligible. Constraint evaluation SHALL fail closed and SHALL apply equally to identity and short-name matches.

#### Scenario: Some variants satisfy constraints
- **WHEN** a matched template group contains eligible and restricted variants
- **THEN** only eligible variants remain available for selection and diagnostics retain the rejected variants

#### Scenario: All raw matches are restricted
- **WHEN** installed templates match the reference but every candidate fails a constraint
- **THEN** resolution returns a restricted outcome with actionable constraint diagnostics rather than not found

#### Scenario: Constraint implementation cannot evaluate
- **WHEN** a constraint returns not evaluated or throws a recognized constraint evaluation failure
- **THEN** the affected candidate is ineligible and the outcome identifies a template or host configuration error

#### Scenario: Force is requested
- **WHEN** invocation permits destructive file replacement
- **THEN** all template constraints remain mandatory and ineligible candidates cannot be selected

### Requirement: Template group identity
Short-name matches SHALL be partitioned by template group identity before variant selection. A template without a group identity SHALL form a singleton group based on its full identity. Multiple eligible groups for one short name SHALL remain ambiguous.

#### Scenario: One short name maps to multiple groups
- **WHEN** the same short name matches eligible templates from different group identities
- **THEN** resolution returns an ambiguous-group outcome that identifies the groups and their templates

#### Scenario: Template has no group identity
- **WHEN** an ungrouped template matches by short name
- **THEN** it does not merge with other ungrouped templates that have different full identities

### Requirement: Immutable progressive candidate filtering
A template group SHALL implement a read-only list contract whose items are eligible, invocation-ready resolved templates. It SHALL expose immutable filtering operations that produce a new group while preserving the original group, item ordering, projected symbol details, and diagnostics. Language filtering SHALL compare canonical language values case-insensitively. Filtering SHALL never restore a template rejected by constraints.

#### Scenario: Group is filtered by language
- **WHEN** a multi-variant group is filtered using the resolved project language
- **THEN** the returned read-only list contains only eligible resolved templates for that language and the original group is unchanged

#### Scenario: Requested language is unavailable
- **WHEN** no eligible candidate supports the requested language
- **THEN** filtering returns an empty group while preserving diagnostics needed for the command to report the available languages

#### Scenario: Group is enumerated
- **WHEN** a command enumerates or indexes a template group
- **THEN** it receives the same eligible resolved templates in stable deterministic order

### Requirement: Command-owned final selection
Every item in a template group SHALL be independently invocation-ready. The integration SHALL NOT automatically select a unique item, apply precedence as an implicit final tiebreaker, or convert a multi-item group into an ambiguity error. The consuming command SHALL decide which filters to apply and whether to invoke, report an error, or prompt for more information based on the resulting item count and its own interaction policy.

#### Scenario: Filtering leaves one template
- **WHEN** command-selected filtering leaves exactly one item
- **THEN** the command can invoke that resolved template directly

#### Scenario: Filtering leaves multiple templates
- **WHEN** command-selected filtering leaves more than one item
- **THEN** the command can apply another filter, report ambiguity, or prompt the user without the integration choosing an item

#### Scenario: Command considers precedence
- **WHEN** a command uses precedence to narrow a group
- **THEN** precedence is applied as an explicit command-selected filter and not as an implicit integration rule

#### Scenario: Filtering leaves no templates
- **WHEN** command-selected filtering leaves an empty group
- **THEN** the command can use the preserved group diagnostics and prior group state to produce a targeted error

### Requirement: Host-specific template metadata
The integration SHALL load the host configuration selected for the `func` host from `.template.config/func.host.json` while projecting each discovered template into a func-owned candidate. It SHALL merge each `symbolInfo` entry with the corresponding canonical parameter symbol and expose an immutable parameter definition containing the canonical name, effective `longName`, optional `shortName`, type, choices, default value, required status, description, `isHidden`, and `alwaysShow`. Host aliases and visibility SHALL affect command presentation and parsing without changing canonical symbol identity, defaults, bindings, or constraints.

#### Scenario: Parameter has func aliases
- **WHEN** `func.host.json` assigns long and short names to a canonical parameter symbol
- **THEN** every candidate containing that symbol exposes its canonical name, `longName`, and `shortName` for command parsing

#### Scenario: Parameter has no long-name override
- **WHEN** a canonical parameter has no `longName` in `func.host.json`
- **THEN** its effective `longName` is derived from the canonical symbol name

#### Scenario: Parameter suppresses its short alias
- **WHEN** a symbol mapping supplies an empty `shortName`
- **THEN** the projected parameter has no short alias

#### Scenario: Parameter is hidden
- **WHEN** `func.host.json` marks a parameter as hidden
- **THEN** the parameter remains parseable but is omitted from ordinary template details

#### Scenario: Template is hidden
- **WHEN** `func.host.json` marks the template as hidden
- **THEN** ordinary template listing and combined help omit it while exact identity resolution can still address it

#### Scenario: Host metadata is malformed
- **WHEN** the selected `func.host.json` cannot be parsed or references invalid symbol metadata
- **THEN** the affected candidate is not invocable and its diagnostics identify the host metadata failure

### Requirement: Candidate and resolved-template symbol details
Every resolved template in a template group SHALL expose the immutable projected parameter definitions needed to parse its arguments, render help and diagnostics, and invoke that template using one symbol contract. Bind symbols and other non-parameter symbols SHALL NOT be exposed as command parameters.

#### Scenario: Command prepares item-specific parsing
- **WHEN** a template group contains resolved templates with different parameter mappings
- **THEN** the command layer can read each item's canonical names, `longName` values, `shortName` values, validation metadata, and visibility without reading `func.host.json`

#### Scenario: Eligible template enters a group
- **WHEN** a discovered template satisfies its constraints and has valid projected metadata
- **THEN** the group contains a resolved template carrying that template's exact immutable parameter definitions

#### Scenario: Template declares a bind symbol
- **WHEN** template configuration contains a bind symbol
- **THEN** that symbol remains available to template processing but is absent from candidate command parameter definitions

### Requirement: Opt-in func context bindings
Templates SHALL be able to opt in to resolved func context using bind symbols such as `host:func:stack`, `host:func:language`, `host:func:bundle-id`, and `host:func:bundle-version`. Bind symbols SHALL remain internal template values, SHALL NOT become command options, and SHALL NOT replace direct host-context evaluation by compatibility constraints.

#### Scenario: Template declares a func bind symbol
- **WHEN** an eligible template binds a symbol to `host:func:bundle-version`
- **THEN** template processing receives the resolved bundle version without exposing a user-settable option

#### Scenario: Template does not declare a binding
- **WHEN** a template does not declare a func context bind symbol
- **THEN** the integration does not inject a template symbol on its behalf

### Requirement: Candidate-specific argument contract
The integration SHALL expose each resolved template's canonical parameter definitions and func host aliases so a command-layer parser can perform strict item-specific parsing. The command-layer parser SHALL consume projected metadata and SHALL NOT parse `func.host.json` directly. Unknown aliases, missing values, invalid choices, invalid types, and missing required parameters SHALL prevent invocation rather than being ignored or replaced by defaults.

#### Scenario: Candidate parameter is valid
- **WHEN** the command layer maps an item-specific alias to a canonical symbol and validates its value
- **THEN** invocation receives the value keyed by canonical symbol name

#### Scenario: Candidate parameter is unknown
- **WHEN** no remaining resolved template recognizes a supplied template option
- **THEN** execution returns an invalid-arguments outcome and does not invoke a template

#### Scenario: Candidate parameter value is invalid
- **WHEN** a supplied value violates the selected parameter's type, choice, or required-value contract
- **THEN** execution returns an invalid-arguments outcome with item-specific diagnostics

### Requirement: Self-contained resolved template invocation
An invocable resolved template SHALL carry the selected template, approved constraint state, host metadata, and command-scoped invocation plumbing required to invoke itself asynchronously. Invocation SHALL accept an output location, canonical template parameter values, file-conflict policy, and cancellation token, and SHALL return a func-owned result without requiring the caller to pass the template back to the integration entry point.

#### Scenario: Resolved template is invoked
- **WHEN** a caller invokes a resolved template with valid canonical parameters
- **THEN** that exact selected template is instantiated using the originating command-scoped engine environment

#### Scenario: Invocation is canceled
- **WHEN** cancellation is requested during template processing
- **THEN** invocation stops promptly and propagates cancellation

#### Scenario: Resolved template outlives its command scope
- **WHEN** invocation is attempted after the originating command-scoped integration has been disposed
- **THEN** invocation fails explicitly and does not create a replacement environment

### Requirement: Structured integration outcomes
Listing, group discovery, and invocation SHALL return func-owned result models and diagnostics rather than writing directly to the console. The models SHALL provide the information commands need to distinguish not found, restricted, constraint evaluation failure, multiple matching groups, empty filtered groups, multiple remaining templates, invalid arguments, destructive file conflict, cancellation, and successful creation.

#### Scenario: Command renders an integration failure
- **WHEN** resolution or invocation produces a non-success outcome
- **THEN** the command layer can render a targeted message and next action without inspecting template engine implementation types

#### Scenario: Template creation succeeds
- **WHEN** the selected template is instantiated successfully
- **THEN** the result identifies created or changed outputs and any post-actions needed by the command layer
