# template-engine-host

Hosting the Microsoft templating engine inside the func CLI: host identity,
engine-managed template acquisition, the template cache as installed-state
truth, constraint components, post-action policy, bind sources, and
security posture. (Design: D1, D6, D11, D16, D20, D21, D24; design.md
§2.1–§2.3, §2A, §3.)

## ADDED Requirements

### Requirement: CLI-internal engine hosting
The func CLI SHALL host the Microsoft templating engine
(`Microsoft.TemplateEngine.Edge` + `Orchestrator.RunnableProjects`)
in-process, compiled into the CLI binary, with host identifier `func` and
the host version equal to the CLI version. The engine SHALL NOT be
delivered as a workload, and no engine assembly SHALL be loaded from a
template package. The orchestrator SHALL consume the engine through a
CLI-internal service directly; no engine-provider abstraction is exposed.

#### Scenario: Engine available without external dependencies
- **WHEN** `func new` runs on a machine with no `dotnet` CLI installed
- **THEN** template listing and scaffolding for every stack (including
  .NET) succeed using only the func binary and installed template packages

#### Scenario: Single-file publish integrity
- **WHEN** the CLI is published single-file
- **THEN** the engine ships inside the single binary with no loose
  assemblies (guarded by the existing `VerifySingleFilePublish` target)

### Requirement: Engine-managed template acquisition
Template packages SHALL be installed, updated, and uninstalled exclusively
through the engine's `TemplatePackageManager` against a func-owned hive
under the CLI home (`~/.azure-functions/template-engine/func/…`), isolated
from the `dotnet new` cache (`~/.templateengine/dotnetcli`). The engine's
package store and template cache SHALL be the source of truth for what is
installed. Template packages SHALL NOT be recorded in the workload
registry, wrapped as workloads, or carry `workload.json`.

#### Scenario: Install lands in the func hive
- **WHEN** a template package is installed through the CLI
- **THEN** it is acquired into the func-owned hive and its templates
  surface in the engine cache; the workload registry and the user's
  `dotnet new` cache are unchanged

#### Scenario: dotnet new templates are not visible
- **WHEN** a user has templates installed only via `dotnet new install`
- **THEN** those templates are not surfaced by `func new`

#### Scenario: Uninstall removes visibility
- **WHEN** an installed template package is uninstalled through the CLI
- **THEN** its templates no longer appear in any `func new`/`func init`
  surface

#### Scenario: Update replaces with newer version
- **WHEN** an update operation finds a newer version of an installed
  package on the configured source
- **THEN** the newer version is installed and becomes the version the
  cache serves

### Requirement: Template cache behavior
The engine's template cache SHALL be scoped per CLI version under the func
hive, maintained by the engine as part of install/uninstall/update. All
offline read paths (list, per-template help, scaffold) SHALL be served
from the cache without network I/O. A corrupt or format-incompatible
cache SHALL be rebuilt transparently from installed packages.

#### Scenario: Offline read paths
- **WHEN** `func new --list` runs with no network connectivity
- **THEN** installed templates are listed from the cache

#### Scenario: Corrupt cache self-heals
- **WHEN** the cache file is truncated or unparsable
- **THEN** the next engine use rebuilds it from installed packages with no
  user-visible error

### Requirement: Constraint components
The func host SHALL register a custom `func-extension-bundle` template
constraint that evaluates a template-declared extension-bundle requirement
(`{ id, version-range }`) against the project's resolved extension bundle,
supplied by the CLI via host context. The engine's built-in `host` and
`os` constraints SHALL also be available. Constraint evaluation results
SHALL be surfaced to the orchestrator so restricted templates can be
hidden and their call-to-action rendered (see `template-scaffolding`).

#### Scenario: Bundle constraint evaluated against project bundle
- **WHEN** a template declares a `func-extension-bundle` constraint with
  range `[4.42.0,)` and the project's resolved bundle is `4.32.0`
- **THEN** the constraint evaluates as restricted for that project

#### Scenario: Foreign host degrades gracefully
- **WHEN** the same template package is used under plain `dotnet new`
  (where the func constraint component is not loaded)
- **THEN** the template is treated as restricted there rather than
  erroring, per the engine's unknown-constraint behavior

### Requirement: Post-action allowlist
The func host SHALL register exactly these post-action processors: the
func-owned append-to-target-file action, the standard manual-instructions
display action (`AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C`), and the standard
add package/project reference action
(`B17581D1-C5C9-4489-8F0A-004BE667B814`, implemented as a targeted project
file XML edit). All other post actions SHALL be unregistered and degrade
to their `manualInstructions` text (skipping silently when empty).

#### Scenario: Add-reference action runs for .NET templates
- **WHEN** an isolated-worker template declares the add-reference post
  action for an extension package
- **THEN** the func host inserts the `PackageReference` into the user's
  project file, idempotently (skipped if already present)

#### Scenario: Unsupported action degrades
- **WHEN** a template declares the run-script post action
- **THEN** no script executes; its manual instructions are printed instead

### Requirement: No code execution from template content
Template packages SHALL be treated as data only. The func host SHALL NOT
load assemblies, components, or generators from installed template
packages; the engine component set is fixed at host construction.

#### Scenario: Malicious payload cannot execute
- **WHEN** an installed template package contains an assembly or a
  template referencing a non-built-in component
- **THEN** no code from the package is loaded or executed; unresolvable
  components fail that template's use with a diagnostic

### Requirement: msbuild bind-symbol source
The func host SHALL register a bind-symbol source answering `msbuild:`
prefixed bindings by reading the corresponding property (e.g.
`TargetFramework`) from the project file in the working directory.

#### Scenario: TFM-conditional content resolves correctly
- **WHEN** a .NET template binds `msbuild:TargetFramework` and the user's
  project targets `net6.0`
- **THEN** the bound value is `net6.0` and TFM-conditional symbols select
  the package versions appropriate for `net6.0` (not the template's
  default TFM)
