## Why

The v5 `func` CLI carries two bespoke template engines — `Templates.V2` (a custom
`NewTemplate[]` jobs/actions DSL for Node/Python) and `Templates.DotNet` (an offline
catalog plus a `dotnet new` shell-out against a CLI-provisioned hive). Each has its own
schema, packaging shape, gating logic, and channel machinery. This is duplicated effort,
a maintenance burden, and a barrier to adding new stacks. Microsoft.TemplateEngine — the
engine `dotnet new` itself already delegates to — can host all of this in-process as a
single universal engine, so the CLI owns one template model instead of three.

## What Changes

- **BREAKING** Replace `Templates.V2` and `Templates.DotNet` with a single in-process
  Microsoft.TemplateEngine host. Both existing engine providers and the
  `ITemplateEngineProvider` extension point are removed — the universal engine is the
  only way to run templates.
- **BREAKING** Templates become standard `.template.config/template.json` packages.
  The V2 jobs/actions DSL and the `dotnet-templates.json` projection are retired.
- Template **gating** moves entirely into `template.json` `constraints` (source of truth):
  a custom `func-extension-bundle` constraint plus the built-in `host`/`os` constraints.
  Stack and language become soft **selection tags**, not constraints.
- `func new <id>` becomes stack-agnostic via `groupIdentity` + a shared `shortName`;
  the CLI resolves the target stack/language from the working directory, with
  `--language` as the only explicit override. No `--stack` flag.
- **BREAKING** The `channel` axis (stable/preview/experimental prerelease-label →
  bundle-id mapping, per-channel pack-time subsetting, orchestrator channel-match) is
  removed. A template's bundle requirement is declared per-template in its constraint;
  the project's `host.json` bundle decides visibility.
- Template **install** moves to Microsoft.TemplateEngine's `TemplatePackageManager`
  writing into a func-owned, isolated settings hive (never the `dotnet new` cache).
  A single `func … install` front-door routes template packages to the engine and
  workload packages to the existing workload installer.
- The `func new` orchestrator shrinks to a context-resolver + host-param injector +
  result presenter; the min-bundle gate, stack pre-filter, and `templates-workload.json`
  sidecar are deleted.

## Capabilities

### New Capabilities
- `template-acquisition`: installing, updating, and uninstalling CLI templates via the
  engine's package manager into an isolated func hive, and how a single install
  front-door routes by package type.
- `template-resolution`: how `func new` resolves stack/language from the directory or
  `--language`, groups variants by `groupIdentity`/`shortName`, and selects (or fails
  to select) a single template, including empty-directory behavior.
- `template-gating`: constraint-based visibility rules (bundle/host/os), failure
  precedence and messaging, and the assumed-latest-stable rule when no bundle resolves.

### Modified Capabilities
<!-- No existing openspec specs; all capabilities are new. -->

## Impact

- **Removed projects/code**: `src/Templates.V2`, `src/Templates.DotNet`,
  `ITemplateEngineProvider` + registry, `IInstalledTemplatesWorkloads`,
  `templates-workload.json` generation, min-bundle gate and channel-match logic in
  `NewCommandRunner`, and the DotNet item-template hive provisioner.
- **New dependencies**: `Microsoft.TemplateEngine.Edge` +
  `Microsoft.TemplateEngine.Orchestrator.RunnableProjects` (and abstractions), hosted
  in-process with a custom `ITemplateEngineHost` and settings location.
- **Template packages**: `Azure.Functions.Cli.Workloads.Templates.*` are re-authored as
  standard template packages carrying `template.json` with func constraints; existing
  channel/prerelease conventions become non-authoritative human hints.
- **Commands affected**: `func new` (resolution/gating/apply), a template install
  front-door, and `func new --list` (can now surface constraint call-to-action text).
- **Isolation**: engine settings hive is func-owned and separate from
  `~/.templateengine/dotnetcli`; standard package format keeps templates independently
  `dotnet new install`-able, though func-specific constraints render such templates
  "restricted" outside func.
