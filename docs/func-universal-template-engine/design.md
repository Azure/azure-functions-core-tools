## Context

The v5 `func` CLI currently ships two internal template engines behind
`ITemplateEngineProvider`, dispatched by `EngineId`:

- **`Templates.V2`** — Node/Python. Custom `NewTemplate[]` jobs/actions DSL with
  `$(KEY)` substitution; payload under a content workload's `content/v2/`.
- **`Templates.DotNet`** — isolated .NET. Offline catalog from `dotnet-templates.json`
  for listing/help; scaffolds by shelling out to `dotnet new <shortName>` against a
  CLI-provisioned template hive (`ItemTemplateHiveProvisioner`).

Both are delivered as `kind: content` workloads named
`Azure.Functions.Cli.Workloads.Templates.<Stack>`, gated by a `templates-workload.json`
sidecar (min-bundle) plus a channel model where the package prerelease label maps 1:1 to
an extension-bundle id, with per-channel pack-time template subsetting. The `func new`
orchestrator (`NewCommandRunner`) resolves stack/language, picks a channel, applies the
min-bundle gate and stack pre-filter, then dispatches to an engine.

The key realization driving this change: the DotNet path already *is*
Microsoft.TemplateEngine, just out-of-process. Hosting that engine in-process and
re-expressing Node/Python as standard `template.json` templates collapses three template
models into one.

## Goals / Non-Goals

**Goals:**
- One universal in-process template engine (Microsoft.TemplateEngine) for all stacks.
- Templates authored as standard `.template.config/template.json` packages.
- Gating expressed entirely as `template.json` constraints (single source of truth).
- `func new <id>` stack-agnostic; stack/language resolved from context with `--language`
  as the only override.
- Template install isolated from the `dotnet new` cache; acquisition via the engine's
  own package manager.
- A materially smaller `func new` orchestrator.

**Non-Goals:**
- Pluggable template *engines* — the universal engine is the only renderer (the existing
  `ITemplateEngineProvider` extension point is removed).
- Pluggable template *sources* (e.g. listing templates straight from a
  `github.com/Azure-Samples` git repo). Tracked separately; this design does not add it.
- Changing `func init` (project creation) behavior beyond what `func new` requires.
- Preserving byte-for-byte compatibility with existing template packages.

## Decisions

### D1: Host Microsoft.TemplateEngine in-process with a func-owned settings hive
Use `Microsoft.TemplateEngine.Edge` + `Orchestrator.RunnableProjects` with a custom
`ITemplateEngineHost` (host identifier `func`) whose settings location is a func-owned
directory, never `~/.templateengine/dotnetcli`.
- *Why*: In-process removes the `dotnet new` shell-out, the hive provisioner, and process
  startup cost; a custom settings location satisfies the isolation requirement for free.
- *Alternatives*: Keep shelling out to `dotnet new` (status quo for .NET) — rejected: keeps
  two models, needs the dotnet SDK on PATH, and can't host Node/Python.

### D2: `template.json` constraints are the source of truth for gating
Gating (bundle/host/os) lives in each template's `constraints`. A custom
`func-extension-bundle` constraint (args `{ id, version-range }`) is registered as an
engine component and evaluates against the project's resolved bundle, supplied via host
params. The built-in `host` constraint (`hostname: func`) makes a template func-only and
degrades gracefully under plain `dotnet new` (hidden, not errored).
- *Why*: Single artifact describes both content and applicability; deletes the sidecar
  manifest, min-bundle gate, and channel-match step. Unmet constraints hide the template
  rather than erroring — ideal UX for a min-bundle gate, and the `CreateRestricted`
  call-to-action can be surfaced.
- *Trade-off*: Custom constraints render templates "restricted" under plain `dotnet new`
  (the func component isn't loaded there). Accepted — bundle-gated templates are inherently
  func-specific.
- *Alternatives*: Keep a func sidecar manifest + orchestrator pre-filter (status quo) —
  rejected: two sources of truth, more machinery, per-package rather than per-template.

### D3: Stack and language are selection tags, not hard constraints
`func-stack` and language are engine tags used for *selection*, not `constraints`. The
orchestrator filters by ambient stack by default; explicit `--language` overrides and can
cross the ambient stack with a non-blocking advisory.
- *Why*: A stack mismatch (e.g. a `.ts` file in a Python app) is an obvious, recoverable
  user choice, not a system failure — it should not hard-block. Making stack a hard
  constraint contradicts allowing `--language` to override it.
- *Alternatives*: `func-stack` as a hard constraint — rejected: it would make an explicit
  `--language` request unusable in a mismatched directory.

### D4: Cross-stack template identity via `groupIdentity` + shared `shortName`
Per-stack variants share a `groupIdentity` and `shortName` (e.g. `httptrigger`). Engine
group resolution narrows the group by ambient stack/language + constraints; the winning
variant carries its own stack, so the language→stack mapping is derived, never hardcoded.
- *Why*: This is exactly the mechanism `dotnet new` uses for C#/F# disambiguation, with
  ambient stack replacing the `--language` axis.

### D5: Stack resolution order = explicit `--language` → ambient → error
`func new` is not project-mandated. Stack resolves from `--language` first, then directory
inference; if neither yields a stack, a hard error directs the user to `func init` or
`--language`. Empty-directory scaffolding is allowed with explicit input (may produce an
incomplete project — the user opted in). Only `--language` is supported; no `--stack`.
- *Why*: Every Functions language maps uniquely to one stack, so `--language` fully
  supplies the stack; a `--stack` flag is redundant.

### D6: Unresolvable bundle assumes latest stable
When no `host.json`/bundle resolves, bundle constraints evaluate against a synthetic
latest-stable bundle context (sourced from the existing `IExtensionBundleResolver`).
- *Why*: Keeps the hard gate meaningful in project-less mode (preview-only templates stay
  hidden) instead of blanket-allowing; matches the most likely intent (a fresh
  latest-stable project).

### D7: Engine's `TemplatePackageManager` owns template install; one front-door routes
Template packages install via `TemplatePackageManager.InstallAsync` into the func hive;
func workloads continue through the workload installer. A single `func … install`
front-door routes by package type (template package → engine; func workload → workload
installer).
- *Why*: Once channel and min-bundle move into constraints, templates carry no
  func-workload-specific metadata — they are standard template packages, and the engine
  already provides acquisition/update/list/uninstall.
- *Alternatives*: Keep templates in the func workload registry — rejected: redundant
  acquisition and a second gating path now that constraints own gating.

### D8: Channel is dissolved
No prerelease-label→bundle-id mapping, no per-channel pack-time subsetting, no
orchestrator channel-match. A template's bundle requirement is per-template in its
constraint; the project's `host.json` bundle decides visibility. Prerelease labels retain
only their native NuGet acquisition role and an optional non-authoritative human hint.
- *Why*: The constraint already expresses everything channel encoded, more granularly
  (per-template, not per-package), and simplifies the build.

## Risks / Trade-offs

- **Loss of `dotnet new` portability for gated templates** → Accepted; templates that need
  func-specific constraints are inherently func-specific. Built-in `host` constraint keeps
  the graceful-degradation path where portability matters.
- **Re-authoring all Node/Python templates from the V2 DSL to `template.json`** → One-time
  migration cost; mitigated by the standard tooling/ecosystem understanding `template.json`.
- **Two install backends behind one verb could confuse users/diagnostics** → Mitigate with
  clear routing by package type and unambiguous messaging about what was installed and how.
- **Empty-directory scaffolds can be incomplete** (no `host.json`/project files) → Accepted;
  user explicitly opts in via `--language`; document the follow-up (`func init`).
- **Per-template bundle labeling drift** — a package's prerelease label may not reflect a
  mix of per-template bundle ranges → The label is explicitly non-authoritative; the
  template constraint is the truth.

## Open Questions

- Q2 (deferred): should a workload be able to contribute a custom template *source*
  (e.g. a git-backed `IInstaller` mount) so templates can be listed straight from
  repositories like `github.com/Azure-Samples`? Cleanly separable from this design.
- Exact shape of the `func … install` front-door surface (new subcommand vs. extending an
  existing verb) and how uninstall/update/list are exposed for templates vs. workloads.
- Whether `func new --list` should render restricted templates with their call-to-action,
  or only eligible templates by default.
