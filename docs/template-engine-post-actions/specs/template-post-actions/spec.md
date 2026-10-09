## Purpose

Defines trusted Functions project finalization and supported ordinary post-actions with CLI-owned launch commands. Package-manager hooks can still execute project code; this contract is not a sandbox.

## ADDED Requirements

### Requirement: Trusted project configuration declaration
The proposed Functions configuration action SHALL use action ID `00e33184-ffc7-43ef-bec0-b1684df8ad56` with required string arguments `primaryOutputIndex`, `stack`, and `language`. The index SHALL be a nonnegative invariant-culture integer string, validated before engine coercion or output mapping. Every active Functions project root discovered independently from the resolved output/effect snapshot SHALL have exactly one active configuration action. The inventory SHALL include parent roots of resolved primary outputs whose final filename is `host.json` and created/modified file effects whose final filename is `host.json`. Both sources SHALL use the retained resolved snapshot and SHALL be normalized and deduplicated using platform path comparison, rather than depend on configuration declarations. The action SHALL be mandatory when active, SHALL reject extra arguments or continue-on-error, and SHALL bind the raw authored primary-output index to the corresponding resolved file rather than a filtered-list position. A referenced root absent from the independent inventory SHALL be rejected.

#### Scenario: Configuration index is malformed
- **WHEN** a raw configuration index is `"-1"`, `"abc"`, or another invalid nonnegative integer string
- **THEN** raw validation rejects it before engine coercion or mapping

#### Scenario: Conditional earlier output is inactive
- **WHEN** an earlier primary output is suppressed and an active configuration action references a later authored index
- **THEN** the action targets that same authored output after resolution, not another project's filtered-list position

#### Scenario: Referenced output is inactive or missing
- **WHEN** an active action has no unique active resolved file for its authored index
- **THEN** preflight rejects it before cleanup or creation

#### Scenario: Configuration action is optional
- **WHEN** a configuration action declares continue-on-error or an optional execution policy
- **THEN** preflight rejects it

#### Scenario: Active project has an inactive configuration action
- **WHEN** a declared Functions project's output remains active but its only configuration action condition is false
- **THEN** preflight rejects the unconfigured project before cleanup or creation

#### Scenario: Generated project has no configuration declaration
- **WHEN** the resolved effects contain `host.json` files for projects A and B but configuration is declared only for A
- **THEN** the independent inventory contains both roots and preflight rejects B before cleanup or creation

#### Scenario: Primary output omitted but file effect creates a project
- **WHEN** a created or modified `host.json` effect has no matching primary-output declaration
- **THEN** its parent remains in the independent project inventory and cannot escape finalization checks

#### Scenario: Output and effect refer to the same host file
- **WHEN** a resolved primary output and file effect identify the same normalized project root
- **THEN** the inventory requires one finalization for that root rather than two

#### Scenario: Primary output is not a host file
- **WHEN** a resolved primary output names `frontend/package.json` and no `host.json` output or effect identifies its parent root
- **THEN** that primary output alone does not add the frontend directory to the Functions project inventory

#### Scenario: Project and its action are both inactive
- **WHEN** both the declared project/output and its configuration action are inactive
- **THEN** the pair does not require a configuration write
- **AND** at least one other active project must still satisfy project-template creation requirements

#### Scenario: Two active declarations configure the same root
- **WHEN** multiple configuration actions are active for one resolved project root
- **THEN** preflight rejects the ambiguity rather than writing configuration twice

#### Scenario: Mutually exclusive declarations configure one active root
- **WHEN** raw declarations reference one active project output and exactly one configuration action is active
- **THEN** the active project is finalized exactly once

### Requirement: Raw action schema is validated
Packaging and installed-template preflight SHALL validate raw action objects and arguments before engine normalization. Incorrect raw argument types and duplicate properties SHALL be rejected, even if engine projection would stringify or replace them. Item templates SHALL NOT declare trusted project configuration actions.

#### Scenario: Numeric authored index is coerced by the engine
- **WHEN** a raw configuration action supplies numeric `primaryOutputIndex` rather than a string
- **THEN** raw validation rejects it rather than accepting the engine's stringified value

#### Scenario: Item template declares project configuration
- **WHEN** an item template declares the trusted configuration action
- **THEN** preflight rejects it instead of ignoring a mandatory action

### Requirement: Evaluated topology remains stable through creation
Preflight and actual creation SHALL use retained evaluated values for all parameters, bindings, generated symbols, conditions, and renames affecting action topology or output paths. If the pinned-engine integration cannot guarantee that retention for a template, the CLI SHALL reject its unsupported unstable topology before cleanup or creation. An unchanged configuration fingerprint SHALL NOT substitute for evaluated-plan identity.

#### Scenario: Generated value affects a project condition
- **WHEN** a generated symbol could change an active project's condition between preview and actual creation
- **THEN** the integration retains the evaluated value or rejects that unsupported topology before cleanup

#### Scenario: Stable parameters drive renames
- **WHEN** retained evaluated values determine the same authored outputs and renamed paths for preflight and creation
- **THEN** configuration plans remain attached to those outputs without a second independent selection

### Requirement: Project roots and effects are validated
The CLI SHALL resolve declared configuration roots from primary-output parents and require them to match the independent project inventory, validate canonical stack/language and combined template/configuration effects, and reject paths escaping the output root, multiple active configuration plans for one root, conflicting filters, direct template writes to `.func/config.json`, and unsupported processors before target mutation. Repeated output/effect observations of one root SHALL be deduplicated rather than rejected. Write-time path checks SHALL use injectable filesystem validation, including links/reparse points.

#### Scenario: Mixed project stacks are valid
- **WHEN** distinct active project roots declare installed supported Node and Python stacks without conflicting explicit filters
- **THEN** each root has its own canonical configuration plan

#### Scenario: Template writes CLI configuration directly
- **WHEN** a planned template effect creates or modifies a project's `.func/config.json`
- **THEN** preflight rejects the template

#### Scenario: Resolved root escapes through a link
- **WHEN** a planned or write-time project root escapes the output root through a link
- **THEN** no configuration is written outside the output root

### Requirement: Mandatory configuration precedes ordinary actions
After successful creation, project configuration SHALL be written atomically in declaration order and SHALL include canonical stack and language. A finalization failure SHALL stop later finalization and all ordinary actions, preserve successful writes/generated files, and report partial completion. The CLI SHALL NOT perform destructive rollback of generated projects.

#### Scenario: Second project finalization fails
- **WHEN** the first project configuration succeeds and the second fails
- **THEN** the first remains configured, later writes and ordinary actions stop, and the command reports partial initialization with nonzero outcome

#### Scenario: All project configurations succeed
- **WHEN** every mandatory configuration action succeeds
- **THEN** supported ordinary actions can run in declaration order

### Requirement: Ordinary restore adapters are explicit
The proposed restore action SHALL use action ID `17a32346-c721-42cd-a520-4d1cada22ba2` and exactly the required raw string arguments `manager` and `primaryOutputIndex`. Manager SHALL name an approved adapter; the index SHALL be a nonnegative invariant-culture integer string. Raw validation SHALL reject missing, non-string, duplicate, or extra arguments before engine coercion. Projection SHALL bind the authored primary-output index before condition filtering and retain that same output identity through conditions and renames; an inactive, missing, ambiguous, or non-file resolved input SHALL fail preflight. Only reviewed managers and CLI-owned executable/argv construction SHALL be supported. Templates SHALL NOT supply arbitrary executables, extra argv, shell fragments, environment overrides, or escaping paths. Windows batch launchers SHALL use a reviewed CLI-owned launcher and quoting contract rather than generic template shell fallback. Missing tools SHALL fail with guidance without automatic installation. Actual input files and working directories SHALL be revalidated for expected kind, containment, and links immediately before each launch.

#### Scenario: Restore index has the wrong raw type
- **WHEN** a raw restore action supplies numeric `primaryOutputIndex` rather than the required string
- **THEN** raw validation rejects it before the engine can coerce it to a string

#### Scenario: Restore input follows a suppressed earlier output
- **WHEN** an earlier authored primary output is inactive and an active restore action references a later authored output
- **THEN** restore remains bound to that later output's identity and resolved renamed path, not its filtered-list position

#### Scenario: Referenced restore input is inactive
- **WHEN** an active restore action references an authored output that has no active resolved file
- **THEN** preflight rejects the action before mutation or process execution

#### Scenario: Restore has arbitrary process arguments
- **WHEN** a restore declaration adds a shell command or extra executable arguments
- **THEN** preflight rejects it

#### Scenario: Unsupported manager is declared
- **WHEN** a template declares a manager with no approved adapter
- **THEN** preflight rejects it rather than using a generic shell runner

#### Scenario: Tool is missing
- **WHEN** an approved restore action cannot resolve its required installed tool
- **THEN** it fails with actionable guidance without provisioning the tool

#### Scenario: Earlier hook changes a later input into a link
- **WHEN** creation or an earlier restore changes a later action's input or working directory into an escaping link
- **THEN** launch-time validation rejects that action before launching its process

### Requirement: Initial item restore scope is output-local
The initial restore action SHALL target only an authored primary-output manager input inside the invocation output root. A containing project discovered above a nested item output SHALL NOT widen that boundary. Restore of an ancestor project manifest SHALL be unsupported and rejected before item creation, with an output-scope diagnostic. A separately agreed typed containing-project target and compatibility qualification SHALL be required before migrating legacy journeys that need ancestor-manifest restore. The item dispatcher SHALL consume only plans that passed this preflight.

#### Scenario: Nested item needs ancestor manifest restore
- **WHEN** an item runs in `project/functions/orders` and its restore plan targets a manager input at `project`
- **THEN** preflight rejects the unsupported ancestor target without creating the item or launching restore

#### Scenario: Nested item has an output-local restore input
- **WHEN** the nested item has a valid authored manager input within its invocation output root
- **THEN** that input can be restored under the unchanged output-local safety rules

### Requirement: Tool identity prevents template shadowing
Every ordinary adapter SHALL resolve and validate absolute paths for its executable, interpreter, batch script, and launcher through the reviewed CLI tool boundary. Resolution SHALL NOT search the generated working directory. Resolved link/reparse targets SHALL NOT originate in the invocation output or template mount. Windows launchers SHALL invoke the exact validated absolute manager-script path rather than a bare command name. Launch-time checks SHALL preserve that tool identity. These checks SHALL NOT claim to sandbox manager hooks or project code.

#### Scenario: Template creates a manager shim
- **WHEN** generated content contains `npm.cmd`, `mvn.cmd`, `go.exe`, or a similarly named tool in the invocation output
- **THEN** the adapter does not select it and invokes only the independently resolved absolute tool path

#### Scenario: Tool link targets generated content
- **WHEN** an apparent installed tool path resolves through a link to template-controlled output
- **THEN** tool validation rejects it before launch

#### Scenario: Valid Windows manager script
- **WHEN** an approved installed manager script and launcher resolve to valid absolute paths outside template-controlled locations
- **THEN** the reviewed launcher invokes that exact script with fixed arguments and tested quoting

### Requirement: Go module tidy is distinct from restore
The proposed Go module-tidy action SHALL use action ID `5080f899-da0c-404a-b80c-909df8e203b5` and exactly one required raw string `primaryOutputIndex` identifying an authored `go.mod` output. The index SHALL be a nonnegative invariant-culture integer string, validated before engine coercion or output mapping. It SHALL preserve that output identity through condition filtering and renames and use the shared path/tool preflight and launch-time rules. The adapter SHALL invoke the absolute Go tool with fixed `mod`, `tidy` arguments from the resolved module root, without template flags or environment overrides. Module/checksum mutation SHALL be reported as ordinary action work, not restore-only behavior or transactional rollback. Legacy initializer-time tidy SHALL remain until default/skip behavior and module effects are qualified and Go/command owners approve the intentional nonzero failure outcome and cancellation contract. Startup-time behavior SHALL remain out of scope.

#### Scenario: Go index is malformed
- **WHEN** a raw Go tidy index is `"-1"`, `"abc"`, or another invalid nonnegative integer string
- **THEN** raw validation rejects it before engine coercion, mapping, or process execution

#### Scenario: Tidy changes module requirements
- **WHEN** the approved tidy action updates `go.mod` or `go.sum` for imports in generated source
- **THEN** the command treats those changes as ordinary module-tidy effects, not a violation of the restore-only contract

#### Scenario: User skips init-time tidy
- **WHEN** the template's reviewed skip-tidy symbol suppresses the ordinary tidy action
- **THEN** mandatory project configuration still completes and no tidy process launches

#### Scenario: Go replacement is not yet qualified
- **WHEN** the proposed tidy adapter lacks agreement or qualification for default/skip, module effects, and the approved failure/cancellation contract
- **THEN** the Go initializer tidy path cannot be removed as completed migration work

#### Scenario: Tidy returns a nonzero exit code
- **WHEN** the approved replacement's tidy process returns nonzero after project creation
- **THEN** the command reports ordinary-action failure with nonzero outcome and preserves generated/configured files
- **AND** migration notes identify this as a change from legacy init's ignored tidy result, not unchanged failure behavior

### Requirement: Existing template action compatibility is qualified
Before switching the current provider, the CLI SHALL inventory standard actions in supported shared item templates and qualify reviewed adapters/translations or replacement packages preserving their behavior. Unsupported actions SHALL remain blocked; silently ignoring them or rejecting current templates without a migration disposition SHALL NOT satisfy command-switch parity.

#### Scenario: Shared item template has an add-reference action
- **WHEN** a currently supported item template declares a standard add-reference action
- **THEN** the switch requires a qualified adapter/translation or replacement template before removing the old provider

### Requirement: Dry-run never executes actions
Dry-run SHALL validate/project the same resolved action plans and ordered effects without configuration writes or process launches. Force SHALL NOT bypass action safety. Rendering a valid dry-run plan SHALL NOT depend on an installed restore tool.

#### Scenario: Dry-run includes restore
- **WHEN** a valid project template has configuration and restore actions but the restore tool is absent
- **THEN** dry-run renders the plans without launching or installing that tool

### Requirement: Ordinary failure and cancellation are explicit
Ordinary actions SHALL run under the command's existing execution policy after required finalization. A failed action SHALL stop later actions unless continue-on-error is true; continued failures SHALL remain reported and produce an overall nonzero outcome. Cancellation SHALL stop later work and pass through process adapters without a claim of atomic rollback of external package-manager state.

#### Scenario: Continued restore failure
- **WHEN** an ordinary action fails with continue-on-error true
- **THEN** later actions run, the failure remains visible, and overall execution is nonzero

#### Scenario: Cancellation follows a successful configuration write
- **WHEN** cancellation is requested after one project is finalized
- **THEN** the successful write remains, subsequent writes/processes stop, and cancellation is propagated

### Requirement: Item execution has a command owner
The proposed `func new` create-mode runner SHALL invoke the shared ordinary-action dispatcher only after successful item-template creation and SHALL report partial file/action outcomes on failure. Explicit create requests, including explicitly non-interactive requests, SHALL follow the reviewed default action policy without a new per-action prompt. Dry-run SHALL never execute actions. Required project configuration SHALL remain prohibited in item templates, and these processors SHALL NOT claim to add missing manifest dependencies.

#### Scenario: Item creation succeeds before restore fails
- **WHEN** an item template is created successfully and its supported restore action fails
- **THEN** the command reports the created files and action failure, returns nonzero, and does not delete the generated item

#### Scenario: Dependency is absent from the manifest
- **WHEN** an authored item template needs a dependency not declared in the manager input
- **THEN** the restore-only action does not claim to add it or fix that template's missing-dependency defect

### Requirement: Packaging validates without executing
Central packaging SHALL validate action metadata and dry-run effects without executing source-controlled scripts, configuration actions, restore processors, or generated applications. Synthesized Samples templates SHALL have one mandatory configuration action per declared project and no ordinary actions. Template authors SHALL own real application end-to-end coverage.

#### Scenario: Authored template requests restore
- **WHEN** central packaging validates an authored template with a supported restore action
- **THEN** it validates the action plan without running its package manager