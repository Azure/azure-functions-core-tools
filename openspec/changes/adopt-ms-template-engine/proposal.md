# Proposal — adopt-ms-template-engine

## Why

The v5 CLI currently maintains **two bespoke templating engines** behind
`func new`: a custom jobs/actions JSON DSL (`Templates.V2`, Node/Python —
whose template corpus the CLI team already hand-authors in-repo) and a
`dotnet new` shell-out against a provisioned template hive
(`Templates.DotNet` — which requires the dotnet CLI on PATH at scaffold
time and a build-time down-projection of upstream `template.json` files).
The Microsoft templating engine (`dotnet/templating`) solves exactly this
problem class, is maintained upstream, is AOT-compatible, and the .NET
Functions item templates are *already* authored in its format — in a repo
we own. Adopting it replaces two engines and two payload formats with one
engine and one format, deletes the dotnet-on-PATH dependency, and gains
engine-native conditions, localization, dry-run, and host-config
mechanisms.

## What Changes

- **New CLI-internal engine host** (`src/Templates.Engine`): hosts
  `Microsoft.TemplateEngine.Edge` + `Orchestrator.RunnableProjects` with a
  func `ITemplateEngineHost` (id `func`), an isolated func-owned hive, an
  allowlisted post-action set (func append, manual instructions, add
  package/project reference), constraint components, and a `msbuild:`
  bind-symbol source.
- **BREAKING — templates are no longer workloads** (pivot 2026-07-30):
  template packages are standard engine packages identified by NuGet
  package types `FuncItemTemplates` / `FuncAppTemplates`, acquired via the
  engine's `TemplatePackageManager` into the func hive; the engine's
  template cache is the installed-state source of truth. No workload
  registry rows, no `workload.json`. Lifecycle folds into `func new`
  (`--install` / `--uninstall` / `--update`); no separate command tree.
- **Template search**: a func-owned discovery service (based on
  `Microsoft.TemplateSearch.TemplateDiscovery`) scans NuGet feeds for the
  func package types and publishes a search index in the engine's
  search-cache format; `func new --search [term] [--source <feed>]`
  queries the index (or a feed directly for local/private sources).
- **Project templates in `func init`**: per-stack `Empty` project
  templates reproduce today's init output; the wizard gains a
  stack-filtered project-template step (`--template <id>` bypasses);
  init auto-installs the official stack packages when missing (`func
  setup` can pre-install); `func new` remains the item-template path and
  never auto-installs.
- **Template content moves to `template.json` format**: Node/Python ship
  authored, human-diffable `.template.config` trees (converted from the
  V2 corpus; authored in core-tools now, migrating to the
  `Functions.Templates` repo later); the .NET official package is the
  upstream `Microsoft.Azure.Functions.Worker.ItemTemplates` lineage with
  func package types + host files added at the source.
- **Removals**: `src/Templates.V2` and `src/Templates.DotNet` are deleted —
  with them the V2 jobs/actions DSL, the `dotnet new` shell-out, the
  template hive and its provisioning step (`func workload install` for the
  DotNet templates workload becomes pure extraction), the build-time
  `dotnet-templates.json` hydration, and `source.json`.
- **Python append flows** are re-expressed as one template + one snippet
  per trigger with a func-owned append post action (create-with-header,
  append-to-app, blueprint create/append via `--file`; duplicate-function
  guard; registration instructions printed, no auto-edit).
- **Per-template CLI hints** (`func.host.json`): option aliases, hidden
  parameters, regex validators, and a function-name validator — engine-
  inert, read by the CLI's option hydrator; added at the source for all
  stacks including the DotNet templates repo we own.
- **BREAKING — gating moves into `template.json` constraints** (reconciled
  with the parallel `func-universal-template-engine` change): a custom
  `func-extension-bundle` constraint per template replaces the channel
  axis (prerelease-label mapping, per-channel packages, channel-match
  step), the `templates-workload.json` sidecar, the `minBundle:` tag, and
  the Node pack-time subsetting pipeline. Unmet constraints hide the
  template with a call-to-action instead of erroring after selection.
  `MissingExtensionBundle` remains a hard error.
- **BREAKING (display only)**: `func new --list` shows unified,
  upstream-style template ids across all stacks (`http`, `timer`, …); the
  `HttpTrigger`-style and legacy suffixed ids remain accepted as hidden
  shortName aliases, so existing scripts keep working.
- **`ITemplateEngineProvider` removed**: the provider seam, its registry,
  and `EngineIds` are deleted; the orchestrator calls the CLI-internal
  engine service directly.
- **Unchanged**: the `func new` scaffold pipeline surface (two-stage
  parse, picker, `--list`, JSON envelope), the project mandate and
  no-`--language` posture. Offline scaffolding remains fully served from
  installed packages; network is used only for search, install, update,
  and init's first-run auto-install.

## Capabilities

### New Capabilities

- `template-engine-host`: hosting the Microsoft templating engine inside
  the func CLI — host identity, engine-managed acquisition into the
  isolated func hive (engine cache as installed-state truth), constraint
  components (`func-extension-bundle` + built-in host/os), post-action
  allowlist and processors (append, add-reference, manual instructions),
  `msbuild:` bind source, security posture (no assembly loading from
  template content).
- `template-packages`: the contract for func template packages —
  identification by `FuncItemTemplates`/`FuncAppTemplates` package types,
  standard engine layout, unified cross-stack template ids with legacy
  aliases, per-template constraint gating, `func.host.json` schema,
  official per-stack packages.
- `template-scaffolding`: `func new` behavior on the new engine — catalog
  and per-template help sourced from live template metadata, unified-id +
  legacy-alias resolution, constraint-gating UX (restricted templates
  hidden with call-to-action), option hydration incl. validators,
  create-flow scaffolding with dry-run conflict detection, Python append
  flow matrix, Created/Modified reporting, package-lifecycle flags
  (`--install`/`--uninstall`/`--update`), failure-path mapping.
- `template-search`: the discovery service (feed scanning by package
  type, engine-based scanning, incremental index publishing in the
  engine's search-cache format) and the CLI search surface
  (`func new --search`, local index override, direct `--source` feed
  queries, installed-state annotations).
- `project-templates`: `func init` integration — per-stack `Empty`
  project templates matching today's init output, the stack-filtered
  project-template wizard step and `--template` parameter, first-run
  auto-install of official packages (plus `func setup` pre-install), and
  CLI-owned post steps (`.func/config.json` stays init-authored).

### Modified Capabilities

_None — no main specs exist yet in this OpenSpec root; the legacy behavior
is documented in `docs/proposed/*.md`, which will be revised separately._

## Impact

- **Code**: new `src/Templates.Engine` (+ tests); `src/Templates.V2` and
  `src/Templates.DotNet` deleted; `src/Func/Templates` orchestrator loses
  engine-zone detection, the channel-match step, and the min-bundle gate;
  `ITemplateEngineProvider`, its registry, and `EngineIds` deleted from
  `src/Abstractions/Templates/`; the templates projects leave
  `src/Workloads/` (they are no longer workloads) and become standard
  template-package projects; `eng/build/Workloads.Templates.targets` and
  the channel-filter scripts are deleted; the workload-enumeration/
  channel-mapping/sidecar-manifest classes in `src/Func/Templates/` are
  deleted.
- **Dependencies**: adds `Microsoft.TemplateEngine.Edge`,
  `Microsoft.TemplateEngine.Orchestrator.RunnableProjects` (+ Abstractions,
  Utils) to the CLI (~2–3 MB binary growth, offset by deleting two engine
  projects; NuGet.* already shipped). Removes the runtime dependency on a
  `dotnet` CLI for .NET scaffolding.
- **External repos & services**: `Functions.Templates` (owned) gains
  `func.host.json` files and the func package types for the .NET
  templates, and later becomes the authoring home for all stacks'
  template.json content (D12 end state). A **new discovery service /
  pipeline** (based on `Microsoft.TemplateSearch.TemplateDiscovery`)
  builds and publishes the func template search index on a recurring
  cadence (hosting URI + ownership: design OQ-22).
- **`func init`**: gains the project-template wizard step, `--template`,
  and first-run auto-install; `func setup` optionally pre-installs
  template packages.
- **User-facing**: no dotnet-on-PATH requirement; faster/simpler DotNet
  templates workload install; three failure modes deleted; short template
  ids in the catalog (aliases preserve compatibility).
- **Docs**: `docs/proposed/templates-workload-spec.md` and
  `docs/proposed/func-new.spec.md` require revision to match (tracked in
  design.md §4).
- **Gate**: implementation starts only after the Task 0 walking-skeleton
  spike (tasks.md) validates single-file hosting, append mechanics, cache
  concurrency, security posture, and output parity.
