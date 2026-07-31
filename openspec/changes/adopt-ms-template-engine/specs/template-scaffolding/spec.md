# template-scaffolding

`func new` behavior on the Microsoft templating engine: catalog and help
surfaces, template resolution, constraint gating UX, option hydration,
create-flow and append-flow scaffolding, result reporting, and failure
mapping. The pre-engine pipeline stages (project/profile gates, language
resolution) are unchanged by this change and are not restated here.
(Design: D2, D13, D16, D18, D19, D20, D21, D24, D26; design.md §2.3, §2.5,
§2A, §6 notes.)

## ADDED Requirements

### Requirement: Template package management folds into func new
`func new` SHALL expose template-package lifecycle operations backed by
the engine's package manager: `--install <pkg[::ver]> [--source <feed>]`,
`--uninstall <pkg>`, and `--update [<pkg> | --all]` (compare installed
versions against the configured source; install newer). These operations
SHALL accept any func template package (item or app types). There is no
separate template-management command tree. Lifecycle and search modes
(`--search`/`--install`/`--uninstall`/`--update`) SHALL bypass the
project and profile gates — only list and scaffold modes require an
init'd project.

#### Scenario: Search works without a project
- **WHEN** `func new --search http` runs in an empty directory
- **THEN** search executes normally; no "run func init first" error

#### Scenario: Install via func new
- **WHEN** `func new --install Contoso.Functions.Templates` runs with
  connectivity
- **THEN** the package installs into the func hive and its item templates
  appear in the next `func new --list`

#### Scenario: Update finds a newer version
- **WHEN** `func new --update --all` runs and an installed package has a
  newer version on the configured source
- **THEN** the newer version is installed and reported; up-to-date
  packages are reported unchanged

### Requirement: Catalog sourced from the engine cache
`func new --list`, the `func new --help` "Available templates" section,
and `--template` tab completion SHALL be served from the engine's template
cache, filtered to the project's stack and resolved language, with
constraint-restricted templates excluded from the selectable set and
language variants deduplicated by `groupIdentity`. Rendering formats and
the `--list` JSON envelope are unchanged.

#### Scenario: Catalog shows unified ids once per trigger
- **WHEN** `func new --list` runs in a TypeScript project with JS and TS
  variants installed
- **THEN** each trigger appears once under its unified id (e.g. `http`)
  with no language suffix

### Requirement: Template resolution by shortName
`--template` SHALL resolve case-insensitively against every declared
`shortName` of the language-filtered catalog, including alias shortNames.
An unmatched id SHALL produce the existing unknown-template error with the
catalog hint.

#### Scenario: Unified id resolves
- **WHEN** `func new --template http` runs in a Python project
- **THEN** the Python HTTP trigger template is selected

#### Scenario: Legacy alias resolves
- **WHEN** `func new --template HttpTrigger-TypeScript` runs in a
  TypeScript project
- **THEN** the same template is selected as for `--template http`

### Requirement: Constraint gating with call-to-action
Templates whose constraints are unmet for the project SHALL be hidden from
selection rather than producing a scaffold error. When a requested id's
only otherwise-matching variant is hidden solely by a bundle constraint,
the CLI SHALL surface that constraint's call-to-action (e.g. update the
`host.json` bundle range). A project whose extension bundle cannot be
resolved at all SHALL retain the existing `MissingExtensionBundle` hard
error (project-mandated scaffolding; no synthetic bundle context).

#### Scenario: Restricted template hidden from catalog
- **WHEN** a template requires bundle `[4.42.0,)` and the project's
  resolved bundle is `4.32.0`
- **THEN** `func new --list` does not offer it for selection

#### Scenario: Bundle-gate call-to-action surfaced
- **WHEN** `func new --template <id>` matches only a variant hidden by a
  bundle constraint
- **THEN** the CLI reports why the template is unavailable and what bundle
  change would make it available, and exits non-zero

#### Scenario: Unresolvable bundle stays a hard error
- **WHEN** the project declares no resolvable extension bundle and a
  bundle-dependent template is requested
- **THEN** the existing `MissingExtensionBundle` failure fires

### Requirement: Option hydration from live template metadata
Stage-B option hydration SHALL read the selected template's parameter
definitions from the engine cache (names, datatypes, defaults, choices,
requiredness) merged with `func.host.json` hints (aliases, hidden flags,
validators). Every visible parameter hydrates as a real typed option;
choice symbols constrain accepted values; validator regexes run at parse
time; hidden symbols hydrate nothing. Template-declared inputs SHALL
require no CLI release to surface.

#### Scenario: New template input needs no CLI change
- **WHEN** a workload revision adds a new parameter symbol to a template
- **THEN** the next `func new --template <id> --help` shows the new option
  with no CLI update

#### Scenario: Template-scoped options are template-scoped
- **WHEN** `--file` is declared (as the `AppFile` symbol) only by Python
  templates and a user passes `--file` with a Node template
- **THEN** stage-B parsing fails with a standard unrecognized-option error

### Requirement: Create-flow scaffolding
For create-flow templates, `func new` SHALL dry-run the instantiation and
fail with the existing `AlreadyExists` behavior when target files exist
and `--force` was not passed; `--force` maps to the engine's overwrite
mode. The template's `sourceName` mechanism SHALL rename output files and
substitute content tokens from `--name`. The rendered result (and
`--output json`) SHALL report the union of engine-created files
(`Created:`) and post-action-modified files (`Modified:`).

#### Scenario: Conflict without force
- **WHEN** `func new -t http -n MyApi` targets a project where
  `src/functions/MyApi.ts` already exists
- **THEN** the command exits non-zero with the `--force` hint and writes
  nothing

#### Scenario: DotNet scaffold reports the csproj edit
- **WHEN** a .NET template's add-reference post action modifies the
  project file
- **THEN** the output lists the scaffolded file under `Created:` and the
  project file under `Modified:`

### Requirement: Python append flows
Python trigger templates SHALL scaffold via one snippet template plus the
func append post action, instantiated into a provider-owned staging
directory so only the append processor touches the project. Flow
resolution: no `--file` and `function_app.py` exists → append bound to
`app`; no `--file` and `function_app.py` missing → create it with the full
app header, then append; `--file <path>` missing → create the file as a
blueprint (header + `bp` binding) and print registration instructions
without editing `function_app.py`; `--file <path>` exists → append bound
to `bp`. A function whose name already exists in the target SHALL fail
the command with no `--force` override.

#### Scenario: Append to existing app file
- **WHEN** `func new -t http -n MyFn` runs in a Python project with an
  existing `function_app.py`
- **THEN** the decorated function is appended bound to `app` and the file
  is reported as `Modified:`

#### Scenario: Blueprint creation prints registration steps
- **WHEN** `func new -t http -n MyFn --file api.py` runs and `api.py` does
  not exist
- **THEN** `api.py` is created with the blueprint header and function, and
  the instructions to import and register the blueprint are printed;
  `function_app.py` is not modified

#### Scenario: Duplicate function name rejected
- **WHEN** the target file already contains `def MyFn(`
- **THEN** the command fails naming the duplicate; `--force` does not
  override

#### Scenario: Failed append orphans nothing
- **WHEN** the append processor fails (e.g. target unwritable)
- **THEN** no staged snippet file remains in the project directory and the
  error hints at the staged content's location for manual recovery

### Requirement: Failure mapping and isolation
Scaffolding on the engine SHALL preserve the existing typed failure UX
while adding: per-template scan isolation (a malformed template is skipped
with a `[packageId]`-prefixed warning; the remaining catalog survives; a
package yielding zero templates produces the reinstall hint), and
post-action degradation semantics (`continueOnError: true` failures warn
with manual-remediation text on an otherwise successful scaffold;
`continueOnError: false` failures fail the command as typed errors).
.NET scaffolding SHALL NOT require a `dotnet` executable, a template hive,
or any provisioning state.

#### Scenario: One bad template does not kill the catalog
- **WHEN** an installed payload contains one malformed `template.json`
- **THEN** `func new --list` shows every other template and prints one
  `[packageId]`-prefixed warning

#### Scenario: Failed csproj edit degrades to a warning
- **WHEN** the add-reference post action cannot locate a project file
- **THEN** the scaffold still succeeds, with a warning naming the package
  and version to add manually
