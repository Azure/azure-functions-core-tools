# template-packages

The contract for func template packages: identification by NuGet package
type, standard engine layout, per-template constraints, `func.host.json`,
and identity conventions. Replaces the former `templates-workload-payload`
capability (templates are no longer workloads — design D21). (Design: D9,
D10, D12, D16, D19, D21, D26; design.md §2A, §2.4, §2.7.)

## ADDED Requirements

### Requirement: Packages identified by func package types
Func template packages SHALL be standard Microsoft.TemplateEngine packages
(templates under `<TemplateName>/.template.config/template.json`)
identified by NuGet package type: **`FuncItemTemplates`** for
function/item templates (consumed by `func new`) and
**`FuncAppTemplates`** for project templates (consumed by `func init`).
A single package MAY declare both types. Packages SHALL NOT carry
`workload.json`, the `FuncCliWorkload` package type, or any workload
packaging metadata. Legacy formats (V2 `NewTemplate[]` DSL,
`dotnet-templates.json`) SHALL NOT be interpreted as template sources.

#### Scenario: Item package identified by type
- **WHEN** a feed package declares the `FuncItemTemplates` package type
- **THEN** the discovery service indexes it and the CLI accepts it for
  install as an item-template source

#### Scenario: Dual-type package
- **WHEN** a package declares both `FuncItemTemplates` and
  `FuncAppTemplates`
- **THEN** its item templates surface in `func new` and its project
  templates surface in `func init`

#### Scenario: Legacy format rejected
- **WHEN** a package contains only a V2 `templates.json` payload
- **THEN** the CLI does not surface any templates from it

### Requirement: Unified template identity scheme
Templates SHALL declare upstream-style short primary `shortName`s that are
uniform across stacks (e.g. `http`, `timer`, `queue`), with the language
encoded in `tags.language` and variants of one trigger within a stack
sharing a `groupIdentity`. `HttpTrigger`-style names and the legacy
language-suffixed ids (e.g. `HttpTrigger-TypeScript`) SHALL be declared as
additional `shortName` aliases for Node/Python templates.

#### Scenario: Same id across stacks
- **WHEN** a user runs `func new --template http` in a Python project and
  again in a Node project
- **THEN** each resolves to that stack's HTTP trigger template

#### Scenario: Legacy id remains valid
- **WHEN** a script passes `--template HttpTrigger-TypeScript` in a
  TypeScript project
- **THEN** the template resolves via the alias exactly as `http` would

### Requirement: Per-template gating declared as constraints
A template with an extension-bundle requirement SHALL declare it in its
`template.json` `constraints` block via the `func-extension-bundle`
constraint (`{ id, version-range }`); templates MAY also use the built-in
`host`/`os` constraints. Packages SHALL NOT encode gating in sidecar
manifests, version prerelease labels, or tags.

#### Scenario: Binding availability expressed per template
- **WHEN** a template's trigger binding exists only in extension bundles
  `>= 4.42.0` of the preview bundle id
- **THEN** its `template.json` declares a `func-extension-bundle`
  constraint for that id and range, and no other artifact encodes the
  requirement

### Requirement: func.host.json contract
`func.host.json` SHALL be an engine-inert, func-owned file inside
`.template.config/` carrying per-symbol CLI hints: `symbolInfo[]` entries
with `id` (symbol name), optional `longName` (CLI option alias), optional
`isHidden` (exclude from hydration), and optional `validator`
(`expression` regex + `errorText`); plus an optional top-level
`functionName.validator`. It SHALL be honored for all stacks, including
the DotNet templates authored in the `Functions.Templates` repo.

#### Scenario: Alias and validator applied
- **WHEN** a template's `func.host.json` maps symbol `AuthLevel` to
  `longName: "auth-level"` and declares a validator on `QueueName`
- **THEN** the hydrated options are `--auth-level` and `--queue-name`,
  and a `--queue-name` value failing the regex produces the declared
  error text at parse time

#### Scenario: Hidden symbol not surfaced
- **WHEN** a symbol is marked `isHidden: true`
- **THEN** it appears in no help output and hydrates no option, while
  remaining settable programmatically by the CLI

### Requirement: Official packages
Node and Python (and later PowerShell) SHALL each have one official
dual-type package `Microsoft.Azure.Functions.Templates.<Stack>` carrying
the stack's curated item templates and its `Empty` project template. The
.NET official packages SHALL be the existing upstream ids —
`Microsoft.Azure.Functions.Worker.ItemTemplates` gaining the
`FuncItemTemplates` package type and
`Microsoft.Azure.Functions.Worker.ProjectTemplates` gaining
`FuncAppTemplates` — with the standard `Template` package type retained
alongside and func host files added at the source. All official packages
use plain semver (no channel prerelease scheme; visibility is decided by
constraints at invocation time).

#### Scenario: One package serves every bundle channel
- **WHEN** the official Node package is published
- **THEN** a single package contains all Node templates, including those
  whose constraints require a preview bundle; visibility is decided at
  invocation time by constraint evaluation, not at pack or install time

#### Scenario: .NET packages keep their identity
- **WHEN** the func package types are added to the upstream .NET
  template packages
- **THEN** existing `dotnet new` consumers are unaffected (the `Template`
  package type remains) and the packages become discoverable and
  installable by func without a rename or republish under new ids
