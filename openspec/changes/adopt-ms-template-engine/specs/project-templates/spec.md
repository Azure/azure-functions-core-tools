# project-templates

Project templates in `func init`: per-stack `Empty` templates, the
wizard's project-template selection step, first-run auto-install, and
CLI-owned post steps. (Design: D23, D25, D27; design.md §2A.)

## ADDED Requirements

### Requirement: Per-stack Empty project templates
Each supported stack SHALL provide an official `Empty` project template
(`tags.type: "project"`, `FuncAppTemplates` package type) whose output
reproduces the current `func init` result for that stack (host.json,
ignore files, stack-appropriate project files, and — per stack — the
starter app file, e.g. Python's `function_app.py`).

#### Scenario: Empty matches current init output
- **WHEN** `func init` scaffolds a Python project via the `Empty` project
  template
- **THEN** the resulting file set is equivalent to today's Python
  `func init` output

### Requirement: Init wizard gains a project-template step
After stack selection (wizard or `--worker-runtime` parameter), `func
init` SHALL offer a project-template selection filtered to the resolved
stack: default `Empty`, plus other installed project templates and index
results for the stack. `--template <id>` SHALL bypass the prompt. In
non-interactive mode without `--template`, `Empty` SHALL be used.

#### Scenario: Default flow unchanged
- **WHEN** a user runs `func init` and accepts defaults
- **THEN** the experience matches today's: pick a stack (and language),
  get the `Empty` scaffold

#### Scenario: Stack-filtered template list
- **WHEN** the user picks Python in the wizard
- **THEN** the project-template step lists only Python-applicable project
  templates (installed and from the index)

#### Scenario: Selecting an uninstalled index entry
- **WHEN** the user selects a project template surfaced from the index
  whose package is not installed
- **THEN** the CLI installs the package (with a clear message) and
  proceeds with scaffolding

#### Scenario: Offline wizard degrades to installed-only
- **WHEN** `func init` runs without connectivity and the official stack
  packages are already installed
- **THEN** the project-template step lists installed templates only, with
  no error from the unavailable index

### Requirement: Constraint context at init time
Because no project exists during `func init`, bundle constraints on
project templates SHALL be evaluated against the latest available
extension bundle (resolved offline from installed bundle content where
possible). This init-scoped rule does not alter `func new`'s posture
(project-resolved bundle; `MissingExtensionBundle` hard error). Official
`Empty` project templates SHALL NOT declare bundle constraints.

#### Scenario: Bundle-constrained project template at init
- **WHEN** an installed project template declares a stable-bundle
  constraint and `func init` lists project templates
- **THEN** the constraint is evaluated against the latest available
  stable bundle rather than failing for lack of a project

### Requirement: First-run auto-install of official packages
When `func init` resolves a stack whose official template packages are
not installed, the CLI SHALL auto-install them from the configured source
with a clear message before scaffolding; if offline at that moment, init
SHALL fail with an actionable error naming the packages. `func setup` MAY
pre-install template packages as part of profile-driven setup. `func new`
SHALL NOT auto-install (missing templates produce an install hint).

#### Scenario: Fresh machine init
- **WHEN** `func init` runs for Python on a machine with no template
  packages installed and with network connectivity
- **THEN** the official Python template package is installed
  automatically (message shown) and init proceeds

#### Scenario: Fresh machine offline
- **WHEN** the same init runs without connectivity
- **THEN** init fails with an error naming the package to install and how

#### Scenario: func new does not auto-install
- **WHEN** `func new` runs in a project whose stack has no installed item
  templates
- **THEN** the command exits non-zero with an install hint and performs no
  network I/O

### Requirement: Thin stack contract and init orchestration
`func init` core SHALL own the init pipeline: stack resolution (from
installed stack workloads' metadata), language resolution, official
template package presence (auto-install), project-template selection,
engine scaffolding, and configuration write. The stack workload's
initializer contract SHALL carry metadata only — stack id, worker-runtime
aliases, display name, supported languages/aliases, the default
function-name validator, and the stack's official template package ids —
with no scaffolding code and no init-option contributions. Per-stack init
options SHALL derive from the selected project template's parameter
symbols, hydrated with the same mechanism as `func new` template options.
.NET project scaffolding SHALL NOT shell out to `dotnet new`.

#### Scenario: Init option comes from the template
- **WHEN** a stack's `Empty` template declares a choice symbol (e.g. a
  target-framework choice)
- **THEN** `func init` surfaces it as a typed option/prompt with no
  stack-workload code involved

#### Scenario: Stack without an Empty package
- **WHEN** a stack workload is installed but no official template package
  exists for that stack
- **THEN** `func init` for that stack fails with an actionable error
  naming the missing package

#### Scenario: No dotnet shell-out at init
- **WHEN** `func init` scaffolds a .NET project
- **THEN** the project comes from the engine applying the upstream
  project template in-process; no `dotnet` process is spawned

### Requirement: CLI-owned post steps
Project-template scaffolding SHALL NOT replace init's CLI-owned
responsibilities: `func init` itself writes `.func/config.json`
(stack/language pin) and performs any CLI-level setup hints. Templates
SHALL NOT author CLI configuration files.

#### Scenario: Config written by init, not the template
- **WHEN** any project template is scaffolded via `func init`
- **THEN** `.func/config.json` content comes from init's own logic, and a
  template attempting to ship a `.func/config.json` file does not
  override it
