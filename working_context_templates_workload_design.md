---
topic: Templates workload (func new redesign)
owner: Naren Soni
started: 2026-05-20
status: design
last_touched: 2026-05-22
synced_to_commit: adf0eb05 (vnext, 2026-05-22)
---

<!--
HOW TO USE THIS DOCUMENT

This file is the durable context for one piece of work. Read top → bottom.
Sections are ordered from "orient yourself" to "open work."

Sections and what goes where:
  §0 Start here       — one-paragraph summary + literal next action.
  §1 Status           — current phase, focus, blockers.
  §2 Key findings     — non-obvious things learned during investigation.
  §3 Design           — captured understanding of how the EXISTING (vnext)
                        system works. Reference material. Descriptive, not
                        prescriptive. Update when the system changes.
  §3B Legacy func new — the main-branch `func new` surface this work
                        replaces. Reference spec; the providers must cover
                        it. Lives on `main`, NOT vnext.
  §4 Decisions        — choices made about OUR work. Append-only log.
  §5 Open questions   — things we don't yet know the answer to.
  §6 TODOs            — concrete next actions.
  §7 Progress         — high-level milestones.
  §8 Notes            — free-form scratchpad. Append-only.
  §9 References       — files, PRs, docs, glossary.

Conventions:
- Dates: ISO YYYY-MM-DD.
- Log entries: `YYYY-MM-DD (who) — one line.` `who` = agent / user / consensus.
- Code refs: `path/to/file.cs:line` so they're clickable.
- Cross-link with [[anchor]].
- Mark uncertainty: 🟢 verified, 🟡 unconfirmed, 🔴 blocker.
- Reverse a decision: strikethrough the old entry, add a new one citing why.
  Don't delete — reasoning that turned out wrong is more valuable than
  clean history.
- When syncing to a new vnext commit, bump `synced_to_commit` in frontmatter
  and append a dated entry to §8 Notes summarizing the delta.
-->

# 0. Start here

**One-paragraph summary.**
Designing a `func new` Templates workload for the vnext CLI. The CLI host
already ships a placeholder `NewCommand`; this work fills in the
contribution point (provisionally `IFunctionTemplateProvider`) and
implements per-stack providers. The new project model, worker/content
workloads, bundle resolver, and Host workload have all landed since the
design started — `func new` plugs into a much more complete platform now.

**Pick this up by:** reading §1 Status, then §4 Decisions for the last
choice made. §3 Design is the reference frame — re-synced to vnext
@adf0eb05 on 2026-05-22.

---

# 1. Status

- **Phase:** design
- **Current focus:** picking workload shape + template schema + storage
  model. The DotNet initializer's `dotnet new` + template-hive approach
  (now merged, #5014) is a strong precedent for at least the .NET
  template provider.
- **Blocked on:** nothing.
- **Last touched:** 2026-05-22 — re-synced §3 Design to vnext @adf0eb05.
  Captured: WorkloadInfo type hierarchy, content workloads in inventory,
  bundle resolver in core, Host workload, run/start rename,
  GetInitOptions registry, DotNet initializer.

---

# 2. Key findings

Non-obvious things learned during investigation. Each cites source.

- 🟢 **`func new` already exists as a built-in placeholder.**
  `src/Func/Commands/NewCommand.cs` — emits a `NoWorkloadsInstalled`
  hint and exits 1. Source comment describes how it should evolve.
  So the architectural decision "where does `func new` live" is already
  made: it's a built-in command, not a workload-contributed one.

- 🟢 **The pre-#5049 `IProjectResolver` model is gone.**
  Replaced by `IFunctionsProjectFactory` + `FunctionsProject` + a
  separate `IFunctionsWorkerResolver`. Anything mentioning
  `IProjectResolver` is stale.

- 🟢 **`func init`'s option-collision problem is solved (#5070).**
  `IProjectInitializer.GetInitOptions` now takes an `IInitOptionRegistry`;
  workloads call `registry.GetOrAdd(option)` so shared options (e.g.
  `--no-bundles`) appear once in `--help` and resolve to the same
  instance across workloads. Shared option factories live in the new
  `CommonInitOptions` (`--no-bundles`, `--bundles-channel`). **The
  Templates `GetNewOptions()` analogue should follow this same registry +
  shared-factory pattern** — don't reintroduce the collision bug. [[3.5a]]

- 🟢 **Bundle channel is not new, but churned + centralized.**
  `BundleChannel { GA, Preview, Experimental }` selects the bundle
  package id at init. The `--bundles-channel` option was removed (#5060),
  reverted (#5063), and extended to Go (#5064); options moved into
  `CommonInitOptions`; `--no-bundle` was renamed to `--no-bundles`;
  channel now also acts as the content-workload version axis (#5061).
  Details in [[3.5a]].

- 🟢 **The DotNet initializer is the closest precedent for a .NET
  template provider.** `DotNetProjectInitializer` (#5014) shells out to
  `dotnet new func` from `Microsoft.Azure.Functions.Worker.ProjectTemplates`
  via `IDotnetCliRunner` + `IDotnetPathResolver`, caching templates in a
  30-day **template hive** (`ITemplateHivePathProvider`). `func new` for
  .NET will be a thin wrapper around the same `dotnet new` mechanism —
  reinforces the "opaque template handle" design (G1c).

- 🟢 **Content workloads are now first-class in the inventory (#5074).**
  `WorkloadInfo` is an abstract base with `RuntimeWorkloadInfo` (loaded)
  and `ContentWorkloadInfo` (payload-only). `IWorkloadProvider` exposes
  `GetWorkloads()`, `GetRuntimeWorkloads()`, `GetContentWorkloads()`.
  **A Templates *content pack* (Shape C) is now viable** — the inventory
  can surface installed content workloads by id, exactly like the bundle
  scanner does.

- 🟢 **Bundle resolution moved into CLI core (#5036, #5055).**
  `IExtensionBundleResolver` (Abstractions/Bundles) + `ExtensionBundleResolver`
  (Func/Bundles) scan installed content rows. The bundles *workload* is
  now `kind: content`. **This is the reference architecture for a
  "resolver in core + content packs supplying payloads" design**, which
  Templates could mirror if templates ship as content packs.

- 🟢 **`func init` now REQUIRES a resolved stack (#5057). "Skeleton-
  first" is dead.** Previously `func init` wrote a skeleton `host.json`
  and exited 0 even with no/ambiguous workload. As of #5057:
  - No stack resolved (no workloads / no `--stack` match / ambiguous
    non-interactive) → render hint, **exit 1**. No half-initialized dirs.
  - `host.json` is now written by the **workload initializer** via
    `ProjectFiles.WriteIfMissing(host.json, ProjectFiles.MinimalHostJson,
    force)` — the command no longer writes it (`WriteHostJson` removed).
  - The command still writes `.func/config.json` (after a stack resolves).
  - `--force` now **clears the directory** (via `ConfirmClearDirectoryAsync`
    + `ClearDirectory`), not just overwrite-in-place.
  Implication for `func new`: don't assume a bare `host.json` skeleton
  exists from a no-stack init — there's no such thing anymore. The
  project-resolution path ([[3.10]]) is the right gate.

- 🟢 **`func init` writes `.func/config.json` keyed by `StackOptions`.**
  Format: `{ "Stack": { "Runtime": "python", "Language": "python" } }`.
  Cross-command bridge — `func new` can read the stack from here.

- 🟡 **`IWorkloadProvider.FindByStack` was proposed and NOT taken
  (#5025, closed unmerged).** That refactor (`IOptions<StackOptions>` +
  `FindByStack` + reshaped `IWorkloadResolver`) was abandoned when the
  project APIs went the `IFunctionsProjectFactory` direction (#5049).
  `StackOptions` + `IConfigureOptions<StackOptions>` did land separately
  (#5032); `FindByStack` did not. Don't re-propose stack-by-alias lookup
  on `IWorkloadProvider` without checking why #5025 was dropped.

- 🟡 **Go "native worker" work is on a side branch.** #5062 ("Include
  native worker in minified builds") merged to `feature/go-support`, not
  `vnext`. Go's worker story may diverge from Node/Python; verify against
  that branch before designing Go template specifics.

- 🟢 **Closed-unmerged noise (no action):** #5059 (remove
  `--bundles-channel` from init — superseded by the revert saga), #5047
  (worker scaffold — superseded by #5048). Docs-branch CLI reference
  proposals #5050/#5051 were also closed unmerged.

- 🟢 **Two template schemas on `main`: `Template` (legacy) and
  `NewTemplate` (Python v2)** + shared `UserPrompt` system. vnext is the
  moment to unify or formally bifurcate.

- 🟢 **`run` is the canonical host command; `start` is an alias (#5066).**
  Doc/UX copy should prefer `func run`.

- 🟡 **Prototype debris still present.** `CliHostFactory.cs:90-100` hard-
  registers a `"test"` `RuntimeWorkloadInfo` + `DemoProjectFactory`;
  `DotnetWorkload` is still nested in `FunctionsProjectResolver.cs`.
  Not cleaned up as of @adf0eb05.

---

# 3. Design (captured understanding)

Reference material. This is what *exists today* in vnext (@adf0eb05),
not what we plan to build. Treat like API docs — keep it accurate.

## 3.1 Three-layer architecture

```
Func              src/Func/                   the CLI executable
  │ references
  ▼
Abstractions      src/Abstractions/           NuGet contract package
  ▲ referenced by
  │
Workloads         src/Workloads/<role>/<id>/  separate NuGet packages,
                                              loaded at runtime
```

Workloads reference *only* Abstractions. The CLI has no compile-time
reference to any workload. `kind: workload` packages load into their own
collectible `AssemblyLoadContext` (`WorkloadLoadContext`); shared
contract types from Abstractions cross the boundary via the default
context. Anything one component must see from another must live in
Abstractions.

## 3.2 Workload taxonomy — `kind` × role

**Axis: `kind` (loader behavior).** Declared in `workload.json`,
modeled by `WorkloadKind.cs`.

| `kind` | Entry-point? | Payload? | Loader action |
|---|---|---|---|
| `workload` *(default)* | yes | yes | Loads + calls `Configure(builder)` |
| `content` | no | yes | Not activated; surfaced in inventory; resolved by id |
| `meta` | no | no | Never loaded; bundles other packages via nuspec deps |

`SelectLiveEntries` (`WorkloadRegistration.cs`) loads only `kind=Workload`,
highest semver per packageId. **As of #5074, `kind=Content` entries are
materialized into the inventory** (`ContentWorkloadInfo`) even though
they aren't activated — consumers find their payloads by package id.

**Axis: role (what the package provides).**

| Role | Subfolder | `kind` | Provides |
|---|---|---|---|
| Stack | `src/Workloads/Stacks/` | workload | `IProjectInitializer` + `IFunctionsProjectFactory`. Owns project ownership. |
| Worker | `src/Workloads/Workers/` | content | Worker runtime binaries + `worker.config.json`. (Node, Python, Go.) |
| Host | `src/Workloads/Host/` | content | The Azure Functions host shell + payload that `func run` launches. RID-suffixed pkg id; alias `host`. |
| Tools | `src/Workloads/Tools/` | content* | Cross-cutting payloads. ExtensionBundles is now content-only (#5055). |
| Feature | *(none yet)* | workload | Top-level commands via `RegisterCommand`. |

\* ExtensionBundles flipped from `kind: workload` to `kind: content` in
#5055 — its resolution logic now lives in CLI core (see [[3.9]]).

Folder is convention, not enforcement. `kind` + what's registered in
`Configure` classify a package.

## 3.3 Workload package shape

```
<id>.<version>.nupkg
├── workload.json            ← package-root manifest
├── README.md
├── tools/any/               ← runtime payload (kind: workload)
│     <entry>.dll
│     <deps>.json (if transitive deps)
└── content/                 ← static payload (kind: content)
      workers/<runtime>/worker.config.json   (worker workloads)
      bundles/<id>/<version>/                 (bundles workload)
      ...host payload                          (host workload)
```

`workload.json` schema (DisplayName/Description added in #5074 so
content packages can self-describe with no entry class):
```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "workload",                 // or "content" / "meta"
  "entryPoint": {                     // required only for kind=workload
    "assemblyPath": "MyWorkload.dll",
    "type": "FQN.MyWorkload"
  },
  "displayName": "...",               // optional; for inventory / list
  "description": "..."                // optional
}
```

Install extracts selectively (#5065): `workload.json` + `tools/` for
runtime workloads, plus `content/` payloads for content packages.

## 3.4 Workload lifecycle (boot sequence)

`WorkloadRegistration.RegisterWorkloadsAsync` in
`src/Func/Hosting/WorkloadRegistration.cs`:

1. Read `~/.azure-functions/workloads.json` (the global registry).
2. Materialize inventory: `RuntimeWorkloadInfo` for live `kind=Workload`
   (highest semver per id), `ContentWorkloadInfo` for `kind=Content`.
3. For each runtime workload:
   - Load `tools/any/<assemblyPath>` into its own `WorkloadLoadContext`.
   - `Activator.CreateInstance` the entry-point type (must derive from
     `Workload`; parameterless ctor).
   - Construct a workload-scoped `DefaultFunctionsCliBuilder`.
   - Call `workload.Configure(builder)`.
4. Register `IWorkloadProvider`, `IWorkloadInvoker`.
5. Built-in commands resolve their dependencies from DI.
6. Parse + invoke.

Failure isolation: per-workload throws become stderr warnings; remaining
workloads still load. `Configure` is **not transactional** — partial
registration is possible if it throws partway. Boot traced under
`cli.workload.boot` → `cli.workload.boot_duration` histogram.

## 3.5 Contribution surface — `FunctionsCliBuilder`

`src/Abstractions/Workloads/FunctionsCliBuilder.cs`. The only object
handed to `Configure`:

```csharp
public abstract class FunctionsCliBuilder
{
    public abstract IServiceCollection Services { get; }
    public abstract void RegisterCommand(FuncCommand command);
    public abstract void RegisterCommand<TCommand>() where TCommand : FuncCommand;
    public abstract void RegisterCommand(Type commandType);
    public abstract void AddProjectFactory(IFunctionsProjectFactory factory);
}
```

- **`Services`** — direct `IServiceCollection` access (standard MEDI).
- **`RegisterCommand` (3 overloads)** — top-level `func <verb>` subcommand.
- **`AddProjectFactory`** — registers a factory for `func run`'s
  recognition step.

Concrete impl: `DefaultFunctionsCliBuilder` — workload-scoped (carries a
`RuntimeWorkloadInfo`). A workload-less builder used at host bootstrap
throws on `RegisterCommand`/`AddProjectFactory`.

**Initializer option contribution (changed #5070).**
`IProjectInitializer.GetInitOptions` now takes an `IInitOptionRegistry`:

```csharp
public interface IProjectInitializer
{
    string Stack { get; }
    string DisplayName => Stack;                 // added; defaults to Stack
    IReadOnlyList<string> SupportedLanguages { get; }
    IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry);
    Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken ct = default);
}
```
`registry.GetOrAdd(option)` dedups shared options across workloads so
each appears once in `--help` and resolves to a single instance.
**The Templates contribution point should adopt the same registry
pattern for `GetNewOptions(...)`.**

## 3.5a Shared init options + the bundle channel model

**`CommonInitOptions` (new file, #5060/#5063/#5064 churn).**
`src/Abstractions/Projects/CommonInitOptions.cs` centralizes the option
factories that multiple stacks contribute to `func init`, so wording,
default, and alias stay consistent and the registry ([[3.5]]) can collapse
duplicates to one canonical instance:

```csharp
public static class CommonInitOptions
{
    public static Option<bool> NoBundle()
        => new("--no-bundles") { Description = "...", DefaultValueFactory = _ => false };

    public static Option<BundleChannel> BundlesChannel()
        => new("--bundles-channel", "-c")
           { Description = "...", DefaultValueFactory = _ => BundleChannel.GA };
}
```

Each call returns a fresh instance; `IInitOptionRegistry` collapses them
so the flag appears once in `--help` and every stack reads the same
parsed value. Node, Python, and Go all contribute these now (Go added in
#5064). **This is the direct precedent for sharing/deduping
template-specific options across `func new` providers.**

⚠️ **Flag rename:** `--no-bundle` → `--no-bundles` (plural). The
pre-2026-05-20 Python initializer declared `--no-bundle` inline; the
shared factory uses `--no-bundles`.

**Bundle channel model.** `BundleChannel { GA, Preview, Experimental }`
(`src/Abstractions/Projects/ExtensionBundle.cs`). The channel selects
*which bundle package id* lands in `host.json` at init time via
`ExtensionBundle.IdFor(channel)`:

| Channel | Package id |
|---|---|
| GA (default) | `Microsoft.Azure.Functions.ExtensionBundle` |
| Preview | `Microsoft.Azure.Functions.ExtensionBundle.Preview` |
| Experimental | `Microsoft.Azure.Functions.ExtensionBundle.Experimental` |

Default range `[4.*, 5.0.0)` (`ExtensionBundle.DefaultVersionRange`).
Since the bundles workload is now `kind: content` ([[3.9]]), channel also
acts as a **version axis**: the content-workload package version carries a
channel prerelease label (e.g. `4.35.0-preview`). This contrasts with
worker workloads, which have **no channel axis** — worker version = the
upstream worker NuGet version, bare 3-part SemVer (#5061). [[3.10]]

The channel option itself is *not* new (existed at the 2026-05-20 sync);
what changed is centralization into `CommonInitOptions`, the `--no-bundle`
→ `--no-bundles` rename, extension to Go, and channel-as-version-axis for
the content-only bundles workload.

## 3.6 Workload inventory model (refactored #5074)

`src/Func/Workloads/WorkloadInfo.cs`:

```csharp
internal abstract record WorkloadInfo(
    WorkloadKind Kind, string PackageId, string PackageVersion,
    IReadOnlyList<string> Aliases, string InstallDirectory,
    string ContentRoot, string DisplayName, string Description);

internal sealed record RuntimeWorkloadInfo(
    Workload Instance, /* + the base fields */ ...)
    : WorkloadInfo(WorkloadKind.Workload, ...);

internal sealed record ContentWorkloadInfo(/* base fields, no Instance */ ...)
    : WorkloadInfo(WorkloadKind.Content, ...);
```

`IWorkloadProvider`:
```csharp
IReadOnlyList<WorkloadInfo>        GetWorkloads();        // all
IReadOnlyList<RuntimeWorkloadInfo> GetRuntimeWorkloads(); // loaded
IReadOnlyList<ContentWorkloadInfo> GetContentWorkloads(); // payload-only
```

Significance for Templates: a Templates *content pack* would show up via
`GetContentWorkloads()`, discoverable by id + alias + `ContentRoot` —
the same hook the bundle scanner uses.

## 3.7 Contribution registration patterns — bare vs tagged

- **Bare:** `AddSingleton<IFoo, FooImpl>()`; consumer asks
  `IEnumerable<IFoo>`; owning workload not recoverable. Used by
  `IProjectInitializer`.
- **Tagged:** wrap with `RuntimeWorkloadInfo`:
  `AddSingleton(new XRegistration(workload, contrib))`; consumer asks
  `IEnumerable<XRegistration>`. Used by:
  - `WorkloadProjectFactoryRegistration(RuntimeWorkloadInfo, IFunctionsProjectFactory)`
  - `ExternalCommand` adapter (for `RegisterCommand`)

Rule: tag when failure/conflict diagnostics must name the workload.

## 3.8 Built-in command pattern (orchestrator + consumer)

Recipe every built-in follows:

1. Constructor takes `IEnumerable<TContribution>` from DI.
2. Validate set (dedup, empty-set hints).
3. Select one (or filter, or iterate all).
4. Call into the chosen contribution with a typed context.
5. Translate result into exit code + user output.

Built-ins live in `src/Func/Commands/`, mark `IBuiltInCommand`,
register in `BuiltInCommands.cs`. Today: `func` (no args), `version`,
`init`, `new` (placeholder), `run` (alias `start`),
`workload {list,install,uninstall,update,search,prune}`, `help`.

## 3.9 Extension bundle resolution (moved to core #5036, #5055)

Contract `IExtensionBundleResolver` in `src/Abstractions/Bundles/`,
impl `ExtensionBundleResolver` in `src/Func/Bundles/`. Wired into DI in
`CliHostFactory` alongside `InstalledBundleScanner`,
`IInstalledBundleWorkloads`, `IBundleResolveTelemetry`.

Flow: scan installed bundle content rows → intersect host.json range ∩
profile range (`VersionRangeIntersection`) → pick highest satisfying.
Result `ExtensionBundleResolution`:
```
Resolved(BundleId, Version, Path, RuntimeWarning?)
WorkloadMissing(Hint)            // no bundle content workload installed
EmptyIntersection(...)           // host.json ∩ profile = ∅
NoCompatibleInstall(...)         // installed but none satisfy constraint
```
The bundle *payload* now ships as the `kind: content` ExtensionBundles
workload. Consumed by `func run`'s `ValidateExtensionBundleInitializationStep`
(now real). **Pattern to mirror if templates ship as content packs:
resolver in core, payload in a content workload, scan-and-pick by id.**

## 3.10 The project model (post-#5049, unchanged)

`src/Abstractions/Projects/FunctionsProject.cs`:

```csharp
public abstract class FunctionsProject
{
    public abstract WorkingDirectory WorkingDirectory { get; }
    public abstract string StackName { get; }
    public abstract string StackDisplayName { get; }
    public abstract bool   SupportsExtensionBundles { get; }
    public abstract IFunctionsWorker Worker { get; }

    public virtual Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken ct);
    public virtual Task CompleteHostRunAsync(FunctionsProjectHostRunCompletionContext context, CancellationToken ct);
}
```

Created by `IFunctionsProjectFactory.TryCreateProjectAsync(ctx)`:
fingerprint dir → ask injected `IFunctionsWorkerResolver` for the worker
→ pack into `ProjectCreationResult.{Created | NotCreated | Failed}`.
`NotCreated` = try next factory; `Failed` = claimed-but-broken,
short-circuits the resolver loop.

Worker layer (`src/Abstractions/Workers/`): `IFunctionsWorker` +
`IFunctionsWorkerResolver`; failures `MissingCompatibleVersion` /
`NotInstalled`. Worker payloads now ship as `kind: content` worker
workloads (Node/Python/Go), version 1:1 with the upstream worker NuGet,
no channel axis (#5061).

Consumer: `IFunctionsProjectResolver` iterates
`IEnumerable<WorkloadProjectFactoryRegistration>` — first `Created`
wins, first `Failed` short-circuits.

🟡 `DefaultFunctionsWorkerResolver` may still be a stub returning
`NotInstalled` for some/all runtimes until worker resolution against the
new content inventory is fully wired. Verify before relying on
end-to-end `func run`.

## 3.11 The Host workload + `func run` (new #5073, #5066)

`src/Workloads/Host/` — a `kind: content` package shipping the Azure
Functions host as a thin shell (`HostShell`, `FunctionsHostRunner`,
`IFunctionsHostRunner`) plus the host payload. Launch contract
(`Host/README.md`):

- The CLI prepares working dir, env vars, and host args **before** launch.
- The shell consumes `--enable-auth` (else starts with local auth
  bypass) and forwards every other argument unchanged to the host.
- The shell does **not** understand private CLI options (`--port`,
  `--cors`, `--functions`, `--script-root`) — the CLI resolves those to
  the host's env/config surface first.

`func run` is the canonical command (`StartCommand`, name `"run"`,
`Aliases.Add("start")`). Options: `--port`, `--cors`, `--cors-credentials`,
`--functions`, `--no-build`, `--enable-auth`, `--host-version`,
`--output`, `--no-tui`, `--log-file` (+ hidden `--demo-functions`).

## 3.12 Configuration system

`CliConfigurationSourceBuilder` layers, in order:

1. Env vars prefixed `FUNC_CLI_` (`__` → section nesting)
2. `~/.azure-functions/config.json` (global, optional)
3. `local.settings.json` (synthesizes `Stack`/`Host` sections from
   `FUNCTIONS_WORKER_RUNTIME`)
4. `.func/config.json` (per-project pin from `func init`)

Typed options: `StackOptions { Runtime, Language }` (section `"Stack"`),
`HostStartupOptions { Port, Cors, CorsCredentials }` (`"HostStartup"`).
`WorkloadPathsOptions.Home` is the exception — read directly from
`FUNC_CLI_WORKLOADS_HOME`, not via IConfiguration (#5044).

## 3.13 Filesystem conventions

```
~/.azure-functions/
├── workloads.json                       global workload registry
├── workloads/<packageId>/<version>/     extracted package contents
│     ├── workload.json
│     ├── tools/any/                      (kind: workload)
│     └── content/                        (kind: content)
├── profiles/registry.json               profile cache (planned)
└── .version-check                       CLI version-check cache

<project>/
├── host.json                            functions host config
├── local.settings.json                  per-project env + worker runtime
└── .func/config.json                    stack pin from `func init`
```

Who writes what at `func init` (post-#5057):
- **`host.json`** — written by the resolved **workload initializer** via
  `ProjectFiles.WriteIfMissing(host.json, ProjectFiles.MinimalHostJson,
  force)`. Content: `{ "version": "2.0" }`. Node/Python always write it;
  `--no-bundles` only skips the `extensionBundle` merge, not the file.
- **`.func/config.json`** — written by `InitCommand` itself, but only
  after a stack resolves (no resolved stack → exit 1, nothing written).
- **`--force`** — clears the directory (with interactive confirmation via
  `ConfirmClearDirectoryAsync`) before re-initializing; it is not a
  per-file overwrite flag anymore.

Both files are treated as a project marker by `IsAlreadyInitialized` —
either present (without `--force`) makes `func init` refuse. Workloads
adding to `host.json` use `ProjectFiles.MergeHostJson` (merge, not
rewrite). The DotNet template hive lives under the workload home
(`ITemplateHivePathProvider`, 30-day TTL).

## 3.14 Result / failure record pattern

Established idiom: sealed hierarchy via private ctor + named sub-records
+ a static factory class. Used by `ProjectCreationResult`,
`ProjectResolutionResult`, `FunctionsWorkerResolutionResult`,
`FunctionsProjectHostRunOutcome`, `ExtensionBundleResolution`,
`ProjectCreationFailure`, `FunctionsWorkerResolutionFailure`,
`TemplateApplicationResult` (proposed). Pattern-match with `switch`.
No booleans + nullable detail; no exceptions for expected outcomes.

## 3.15 Workload load isolation

Each `kind: workload` loads into its own `WorkloadLoadContext`
(collectible ALC). Incompatible transitive deps don't collide;
workload-internal types aren't visible across boundaries; Abstractions
types are shared identity. `WorkloadLoader` rejects assembly paths
resolving outside the package content root.

## 3.16 The `func run` initialization pipeline

`StartCommand` → `IStartInitializationRunner`. Steps (✅ real / 🚧 stub
as of @adf0eb05):

```
ResolveProfile          🚧 (sets ProfileName = "none")
ResolveConstraints      🚧 (no profile constraints applied)
ResolveFunctionsProject ✅ (IFunctionsProjectResolver)
InstallHostWorkload     🚧
ValidateHostWorkload    🚧
ValidateExtensionBundle ✅ (IExtensionBundleResolver — now real)
PrepareProjectHostRun   ✅ (FunctionsProject.PrepareForHostRunAsync)
StartHost               🚧 (would launch the Host workload shell)
```

State threaded via `StartInitializationState`. Output in three modes
(`compact` TUI / `plain` / `json`) via `OutputModeResolver`. Real host
event source not yet wired (uses `DemoEventSource`).

---

# 3B. Legacy `func new` surface (main — what we're replacing)

⚠️ This describes `main`, **not vnext**. It's the behavioural spec the
new Templates providers must cover. Source:
`src/Cli/func/Actions/LocalActions/CreateFunctionAction.cs` on
`origin/main` (624 lines), class `CreateFunctionAction`.

## 3B.1 Command surface (three registrations)

| Invocation | Context | Notes |
|---|---|---|
| `func new` | global (root) | Primary command |
| `func function new` | `Context.Function` | Same action under the `function` noun |
| `func function create` | `Context.Function` | Alias of the above |

All three resolve to the same `RunAsync`.

## 3B.2 Options

| Option | Alias | Type | Purpose |
|---|---|---|---|
| `--language` | `-l` | string | Template language (C#, F#, JS, TS, Python, …) |
| `--template` | `-t` | string | Template / trigger name (e.g. `HttpTrigger`) |
| `--name` | `-n` | string | Function name |
| `--file` | `-f` | string | Target file name (Python v2 blueprint routing) |
| `--authlevel` | `-a` | enum? | `function` \| `anonymous` \| `admin` (HTTP triggers only) |
| `--csx` | — | bool | Old-style `.csx` dotnet functions |

**Inherited:** `ParseArgs` also calls `_initAction.ParseArgs(args)`, so
**all `func init` options are accepted too** — because `func new` runs
`func init` first when the directory isn't initialized (see 3B.4).

## 3B.3 Special help syntax

`func new <triggerName> help` — when `args.Length == 2` and the 2nd token
is `help`, prints trigger-specific help (JS/TS/Python only) via
`ProcessHelpRequest` instead of scaffolding. Prompts for language/trigger
if missing.

## 3B.4 Decision tree

```
func new
│
├─ [help syntax]  func new <trigger> help → ProcessHelpRequest → return
│
├─ ValidateInputs: if stdin/stdout redirected → require --template + --name
│
├─ ResolveWorkerRuntimeAsync
│     └─ no local.settings.json → run `func init` first,
│        adopt its ResolvedLanguage + ResolvedWorkerRuntime
│
├─ JAVA: WorkerRuntime == Java
│     └─ HARD REJECT ("use Maven") → return
│
├─ Load templates (NeedsToLoadExtensionTemplates) + ResolveLanguageAsync
│
└─ dispatch on (worker runtime × programming model):
   │
   ├─ Branch A — DOTNET non-csx          IsDotnet(rt) && !Csx
   │     template = menu(DotnetHelpers.GetTemplates) or --template
   │     function name prompt (default = template name)
   │     DotnetHelpers.DeployDotnetFunction(...)   ← shells `dotnet new`
   │
   ├─ Branch B — PYTHON v2               IsNewPythonProgrammingModel()
   │     templates = _newTemplates (NewTemplate) + _userPrompts
   │     --file routing:
   │       function_app.py     → job "appendToFile"
   │       existing other file → job "AppendToBlueprint"
   │       new other file      → job "CreateNewBlueprint"
   │     RunUserInputActions(providedInputs, job.Inputs, variables)
   │     _templatesManager.Deploy(templateJob, template, variables)
   │
   └─ Branch C — LEGACY (everything else)
         Node v3, Python v1, PowerShell, Custom, dotnet --csx
         template = menu(GetTriggerNames) or --template
         extension check (Metadata.Extensions + no bundle + no dotnet → err)
         ConfigureAuthorizationLevel(template)  (HTTP + --authlevel)
         function name prompt (default = Metadata.DefaultFunctionName)
         _templatesManager.Deploy(FunctionName, FileName, template)
         PerformPostDeployTasks (TS function.json scriptFile fixup)

Post-deploy awareness messages:
  Python v1 (not v2) → PrintPySteinAwarenessMessage
  Node v3 (not v4)   → PrintV4AwarenessMessage
```

## 3B.5 Sub-decisions

- **Worker runtime** (`ResolveWorkerRuntimeAsync`): no `local.settings.json`
  → run `func init`, inherit lang+runtime; else
  `GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone`.
- **Language** (`ResolveLanguageAsync`): `--language` aligns w/ runtime
  (error on mismatch) → dotnet non-csx infers from project (`*.fsproj` →
  F#, else C#) → dotnet csx filters CSX-capable → non-dotnet menu.
- **Node model** (`IsNewNodeJsProgrammingModel`): `package.json`
  `@azure/functions ^4` → v4; template filter v4 = ids ending `-4.x`,
  v3 = ids not ending `-4.x`.
- **Python model** (`IsNewPythonProgrammingModel`): v2 → `NewTemplate`
  job/action schema; v1 → legacy `Template` schema.

## 3B.6 The three template engines

| Branch | Template source | Deploy mechanism |
|---|---|---|
| A (dotnet) | external `dotnet new func` templates | `DotnetHelpers.DeployDotnetFunction` (shells `dotnet`) |
| B (Python v2) | `ITemplatesManager.NewTemplates` + `UserPrompts` (job/action DSL) | `Deploy(TemplateJob, NewTemplate, variables)` |
| C (legacy) | `ITemplatesManager.Templates` (`Files` + `function.json`) | `Deploy(name, fileName, Template)` |

## 3B.7 Mapping to the vnext Templates design

- Branches A/B/C → three `IFunctionTemplateProvider` implementations
  (dotnet shells `dotnet new` — reuse #5014's runner/hive [[2]]; Python v2
  carries the job/action engine; legacy stacks carry the
  `Files`+`function.json` engine).
- Java hard-reject → simply no Java provider registered → `func new`
  reports "no template provider for this stack" via a workload hint.
- `--authlevel` / `--file` / `--csx` → provider-contributed options via
  `GetNewOptions(IInitOptionRegistry)` [[3.5]], not built-ins.
- "Run init first if not initialized" → in vnext, resolve-or-fail via
  `IFunctionsProjectResolver` [[3.10]] (init is stricter post-#5057 [[3.13]];
  `func new` should not silently auto-init).
- Two template schemas (`Template` vs `NewTemplate`) + `UserPrompt` →
  the schema-unification decision (G1) is still open [[5]].

---

# 4. Decisions

Append-only log of choices made about *our* work (the Templates workload).

Format: `YYYY-MM-DD (who) — DECISION. RATIONALE.`

- 2026-05-20 (consensus) — `func new` stays a built-in command in
  `src/Func/Commands/`. Placeholder already there. Mirrors `func init`.
  Templates workload(s) contribute providers, not commands.

- 2026-05-20 (consensus) — `func new` will reuse
  `IFunctionsProjectResolver.ResolveProjectAsync` for project detection.
  Free: stack name, working dir, graceful refusal in non-Functions dirs.

- 2026-05-22 (agent, pending Naren confirm) — The Templates option-
  contribution method should take an `IInitOptionRegistry`-style
  registry (or a shared analogue), matching the post-#5070
  `GetInitOptions` shape, so template-specific `func new` options dedup
  across providers. RATIONALE: avoids re-introducing the option-collision
  bug that #5070 just fixed for `func init`.

<!--
Decisions still to make:
- Workload shape (per [[3.2]]): templates baked into stack workloads,
  a dedicated Tools workload, content packs (now viable per [[3.6]]),
  or hybrid?
- Template schema: keep main's two schemas, unify, or opaque handles?
  (DotNet's dotnet-new precedent [[2]] argues for opaque handles, G1c.)
- Prompt model: static --options only, two-stage, or provider-owned?
- Storage: embedded in workload assembly, content packs (mirror bundles
  [[3.9]]), CDN download, or hybrid?
- Bare vs tagged registration for IFunctionTemplateProvider.
- Whether to add AddTemplateProvider(...) to FunctionsCliBuilder.
-->

---

# 5. Open questions

Promote to §4 once answered.

- 🟡 Are template-required NuGet extension installs (main's
  `Template.Metadata.Extensions`) still needed, or have extension
  bundles + worker SDK refs made them obsolete in vnext? (Bundle
  resolution is now in core [[3.9]] — leans toward obsolete for
  script stacks.)
- 🟡 Template id namespace: per-provider (`python.HttpTrigger`) or flat?
- 🟡 If templates ship as content packs ([[3.6]]), how is the active
  version picked? Highest-installed (like bundles) or pinned in
  `.func/config.json`?
- 🟡 Should template discovery be `func new --list` / `--info`, or a
  `func templates list/show` subtree?
- 🟡 Does the DotNet template provider reuse the existing template hive
  (`ITemplateHivePathProvider`) from the initializer, or get its own?

---

# 6. TODOs

- [ ] Resolve §5 items with the team
- [ ] Pick workload shape → record in §4
- [ ] Pick template schema option → record in §4
- [ ] Pick prompt model → record in §4
- [ ] Pick storage model → record in §4
- [ ] Draft `IFunctionTemplateProvider` in Abstractions (registry-based
      option contribution per [[3.5]])
- [ ] Draft supporting records (`NewContext`, `FunctionTemplateInfo`,
      `TemplateApplicationResult/Failure`)
- [ ] Decide bare vs tagged registration; if tagged, draft
      `WorkloadTemplateProviderRegistration(RuntimeWorkloadInfo, ...)`
- [ ] Study `DotNetProjectInitializer` + template hive as the .NET
      template-provider blueprint (#5014)
- [ ] If content-pack storage: study `InstalledBundleScanner` /
      `ContentWorkloadInfo` as the discovery blueprint
- [ ] Scope first PR (interface-only? interface + one provider? plus
      `NewCommand` rewrite?)

---

# 7. Progress checklist

- [x] Read existing CLI architecture
- [x] Understand `func init` implementation
- [x] Understand the new project model (PR #5049)
- [x] Reverse-engineer legacy `func new` (main branch)
- [x] Capture architecture understanding in §3 Design
- [x] Re-sync §3 to vnext @adf0eb05 (2026-05-22)
- [ ] Lock design decisions
- [ ] PR 1 — Abstractions: interface + result types
- [ ] PR 2 — Python provider
- [ ] PR 3 — Node provider
- [ ] PR 4 — `NewCommand` orchestrator rewrite
- [ ] PR 5 — DotNet provider (reuse #5014 dotnet-new + hive)
- [ ] PR 6 — Go provider
- [ ] PR 7 — `func templates list/show` (if needed)
- [ ] Docs: `building-a-workload.md` updates for template providers

---

# 8. Notes

Free-form scratchpad. Append-only; don't curate.

- 2026-05-20 (Naren) — Initially investigated `IProjectResolver`; PR
  #5049 deleted it the same day. Verified against latest `vnext` before
  building any design on top.
- 2026-05-20 (Naren) — Profiles feature is stubbed but surface is wired
  into start dashboard banner. Templates design doesn't depend on
  profiles for v1, but `Profile.supportedRuntimes` could eventually
  validate template stack matches active profile.
- 2026-05-22 (agent) — **Re-synced to vnext @adf0eb05** (was b51e70c2,
  +22 commits). Deltas folded into §3:
  - WorkloadInfo → abstract base + RuntimeWorkloadInfo +
    ContentWorkloadInfo; IWorkloadProvider 3 accessors (#5074). [[3.6]]
  - Bundle resolution moved to CLI core (IExtensionBundleResolver);
    bundles workload is now kind: content (#5036, #5055). [[3.9]]
  - New Host workload (kind: content) launched by `func run` (#5073). [[3.11]]
  - Worker workloads now ship real payloads; version 1:1 w/ upstream
    worker NuGet, no channel axis (#5048, #5054, #5061). [[3.10]]
  - `run` canonical, `start` alias (#5066). [[3.11]] [[3.16]]
  - DotNet initializer implemented via `dotnet new func` + 30-day
    template hive (#5014). Strong precedent for .NET template provider. [[2]]
  - `GetInitOptions(IInitOptionRegistry)` + `DisplayName` on
    IProjectInitializer; option dedup (#5070). [[3.5]]
  - Shared init options centralized in `CommonInitOptions`; bundle
    channel churn (removed #5060 / reverted #5063 / Go #5064);
    `--no-bundle` → `--no-bundles` rename; channel-as-version-axis for
    content-only bundles (#5061). [[3.5a]]
  - WorkloadMetadata gained DisplayName/Description; install extracts
    selectively (#5074, #5065). [[3.3]]
  - Prototype debris (`"test"` workload + DemoProjectFactory in
    CliHostFactory; nested DotnetWorkload) still present. [[2]]
  - Two open PRs: #5077 (CI signing/install scripts), #5072 (init
    language aliases).
- 2026-05-22 (agent) — **Extensive PR/commit sweep** (closed + merged +
  other branches). Additional findings beyond the first sync pass:
  - **#5057 reverses "skeleton-first"**: `func init` requires a resolved
    stack; workload initializer writes `host.json`
    (`ProjectFiles.MinimalHostJson`); `--force` clears the directory;
    no-stack paths exit 1. Corrected §2, §3.13, glossary. [[3.13]]
  - **#5025 (closed unmerged)**: `IWorkloadProvider.FindByStack` +
    `IOptions<StackOptions>` refactor abandoned; only `StackOptions` +
    `IConfigureOptions` survived (via #5032). Don't re-propose FindByStack.
  - **#5062** native worker work on `feature/go-support` branch, not vnext.
  - Closed-unmerged superseded PRs: #5059, #5047, #5050/#5051 (docs).
  - #5058 compact-init render fix, #5053/#5052 CI run-cancel, #5046 CI
    release — no design impact.
- 2026-05-22 (Naren + agent) — Captured the legacy `func new` surface
  from `main` (`CreateFunctionAction.cs`) as **§3B**: 3 command
  registrations, 6 own options + inherited init options, the
  `<trigger> help` syntax, the (worker × model) decision tree, and the
  three template engines (dotnet-new / Python-v2 jobs / legacy Files).
  This is the behavioural spec the new providers must cover; mapping to
  the vnext design is in §3B.7.

---

# 9. References

## Files (vnext @adf0eb05)

- `src/Func/Commands/InitCommand.cs` — closest existing pattern
- `src/Func/Commands/NewCommand.cs` — current placeholder we'll replace
- `src/Func/Commands/Start/StartCommand.cs` — `func run` (alias `start`)
- `src/Abstractions/Projects/IProjectInitializer.cs` — analogue interface
  (now `GetInitOptions(IInitOptionRegistry)` + `DisplayName`)
- `src/Abstractions/Projects/IInitOptionRegistry.cs` — option dedup seam
- `src/Abstractions/Projects/CommonInitOptions.cs` — shared init option
  factories (`--no-bundles`, `--bundles-channel`); template-options precedent
- `src/Abstractions/Projects/ExtensionBundle.cs` — `BundleChannel` + `IdFor`
- `src/Abstractions/Projects/FunctionsProject.cs` — project model
- `src/Abstractions/Projects/IFunctionsProjectFactory.cs` — recognize+create
- `src/Abstractions/Bundles/IExtensionBundleResolver.cs` — bundle resolver
- `src/Func/Bundles/ExtensionBundleResolver.cs` — bundle resolver impl
- `src/Func/Workloads/WorkloadInfo.cs` — Runtime/Content inventory types
- `src/Func/Workloads/IWorkloadProvider.cs` — 3-accessor inventory
- `src/Func/Workloads/WorkloadProjectFactoryRegistration.cs` — tagged reg
- `src/Func/Hosting/DefaultFunctionsCliBuilder.cs` — concrete builder
- `src/Func/Hosting/WorkloadRegistration.cs` — boot sequence
- `src/Func/Hosting/CliHostFactory.cs` — DI composition root
- `src/Func/Projects/FunctionsProjectResolver.cs` — tagged-recipe consumer
- `src/Workloads/Host/` — Host content workload (shell + payload)
- `src/Workloads/Workers/{Node,Python,Go}/` — worker content workloads
- `src/Workloads/Tools/ExtensionBundles/` — bundle content workload
- `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs` — dotnet-new
  + template hive (.NET template-provider blueprint)
- `src/Workloads/Stacks/DotNet/ITemplateHivePathProvider.cs`

## Files (main — what we're replacing)

- `src/Cli/func/Actions/LocalActions/CreateFunctionAction.cs`
- `src/Cli/func/Common/Template.cs`
- `src/Cli/func/Common/TemplatesManager.cs`
- `src/Cli/func/Interfaces/ITemplatesManager.cs`

## PRs

Merged since 2026-05-20 (b51e70c2 → adf0eb05):
- #5073 — Initial Host workload (kind: content host shell + payload)
- #5074 — Content workloads in provider inventory (WorkloadInfo split)
- #5069 — init UX: shorter auto-select hint, alias fallback, display names
- #5070 — dedup workload-contributed init options (IInitOptionRegistry)
- #5014 — DotNet IProjectInitializer via `dotnet new` + template hive
- #5066 — `run` canonical, `start` alias
- #5065 — install extracts only workload.json + tools/
- #5057 — Require a stack workload to run `init` (reverses skeleton-first;
  host.json moves to workload initializers; `--force` clears dir)
- #5036 — Bundles resolver in CLI core (IExtensionBundleResolver)
- #5061 — Worker workloads two-axis version scheme
- #5055 — Bundles workload becomes content-only
- #5054 — Pack worker payloads into Node/Python/Go worker workloads
- #5048 — Scaffold Node/Python/Go worker content workloads
- #5060/#5063/#5064 — `--bundles-channel` remove → revert → Go (net: kept)
- #5058 — Fix compact initialization rendering (UI, no design impact)
- #5046/#5052/#5053 — CI (release process, run cancellation) — no impact
- #5067/#5068 — build-workloads skill
- (earlier) #5049 — project APIs refactor (FunctionsProject + factory)

Closed UNMERGED (context only — do not assume present):
- #5025 — IOptions<StackOptions> + IWorkloadProvider.FindByStack
  (abandoned; only StackOptions/IConfigureOptions survived via #5032)
- #5059 — remove `--bundles-channel` from init (superseded by revert)
- #5047 — scaffold worker projects (superseded by #5048)
- #5050/#5051 — docs-branch v5 CLI reference proposals

Other branches:
- #5062 — native worker in minified builds → `feature/go-support`, not vnext

Open against vnext (as of 2026-05-22):
- #5077 (jviau) — CI: sign release files, add install scripts
- #5072 (satvu) — language alias support for `func init`

## Docs

- `docs/building-a-workload.md` — workload authoring guide
- `docs/cli-architecture.md` — runtime architecture
- `docs/repo-structure.md` — folder conventions
- `docs/func-start-json-schema.md` — JSON event schema
- `.github/skills/build-workloads/` + `create-workload/` — workload skills
- `proposed/cli-profiles.md` (on `docs` branch) — profile design spec

## Glossary

- **Workload** — externally-installable extension. `workload` (active),
  `content` (payload, now in inventory), `meta` (bundle).
- **RuntimeWorkloadInfo / ContentWorkloadInfo** — inventory record types
  for loaded vs payload-only packages (#5074).
- **Stack workload** — owns project ownership for one language stack.
- **Worker / Host workload** — `kind: content` packages shipping the
  worker runtime / the Functions host payload.
- **Project factory** — `IFunctionsProjectFactory`: recognizes a
  directory and produces a `FunctionsProject`.
- **Template hive** — cached dotnet-new template store
  (`ITemplateHivePathProvider`, 30-day TTL) used by the DotNet initializer.
- **Tagged registration** — contribution paired with its
  `RuntimeWorkloadInfo`; enables workload-named diagnostics.
- **Skeleton-first** — ⚠️ *superseded by #5057.* Was: `func init` always
  wrote a `host.json` skeleton even with no workload. Now: a resolved
  stack is required; the workload initializer writes `host.json`; no-stack
  paths exit 1. Don't rely on this principle anymore. [[3.13]]
- **MinimalHostJson** — `ProjectFiles.MinimalHostJson` const
  (`{ "version": "2.0" }`); the base `host.json` workload initializers
  write via `ProjectFiles.WriteIfMissing`.
- **`.func/config.json`** — per-project pin written by `func init`;
  bound to `StackOptions`.
- **`IInitOptionRegistry`** — dedup seam for workload-contributed init
  options (#5070); Templates should mirror it.
