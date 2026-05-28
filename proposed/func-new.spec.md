# `func new` spec

> Sister spec to `templates-workload-spec.md` (templates **workload** — payload,
> layout, packaging) and the working-context reference in
> `working_context_templates_workload_design.md` (which has the §3B legacy
> behaviour and §3 vnext platform reference).
>
> **Scope of this doc:** the CLI-side `func new` command — its surface, its
> orchestration pipeline, the contribution point templates workloads plug into,
> and how the V2 / DotNet templating engines are isolated inside the CLI.
> **Out of scope:** template *payload* shape, workload packaging, NuGet tagging,
> StaticContent layout, channel/version axes — those live in the templates
> workload spec.

---

## 1. Goal

Replace the placeholder `NewCommand` in `src/Func/Commands/NewCommand.cs` with
a real `func new` command that:

1. Scaffolds a single function (file(s) + binding metadata) inside an existing
   Functions project by selecting and applying a **template** contributed by an
   installed **templates content workload**.
2. Covers the in-scope `func new` behavioural surface — two template engines
   (.NET hive shellout / v2 `NewTemplate[]` job/action DSL for Node and Python)
   folded behind one CLI command. The v5 templates workloads ship **v2 only**;
   the legacy V1 `Files`-map engine is not part of the vnext CLI
   (templates-workload-spec.md §5.2: "v1 (legacy) programming-model templates
   are not shipped in v5 templates workloads").
3. Drives the picker / option-collection / file-write pipeline from the **CLI
   core**, not from each workload — workloads only contribute *template
   payloads* and *metadata*, never commands.
4. Keeps the v2 (Node/Python `NewTemplate[]` job/action DSL) and DotNet
   (`dotnet-templates.json` + shell-out) templating engines in **separate
   CLI-internal projects**, behind one provider abstraction, so future
   templating engines can be added without touching `NewCommand`.
5. Hydrates per-template options (e.g. `--auth-level` for HTTP triggers,
   `--file` for Python v2 blueprint routing) from template metadata, so the
   user sees only the options that apply to the chosen template.
6. Honours the profile system ([[wcdesign §3.17]]) — refuses to scaffold for a
   stack the active profile doesn't list under `SupportedRuntimes`.

## 2. Non-goals

- **No auto-`func init`.** Legacy `func new` ran `func init` first when the
  directory wasn't initialised. Post-#5057 init is stricter, and we don't want
  to silently create projects from inside `func new` — fail with a clear hint
  instead (see §4.4).
- **No template payload format design here.** Template files and their
  on-disk/in-memory shape (`Template` vs `NewTemplate`, `Files` map,
  `UserPrompt`s, `dotnet new` opaque handle) come straight from the templates
  workload — `func new` just consumes them.
- **No template discovery via `func new`.** `func new --list` is **not** part
  of this surface (decided 2026-05-27). Template discovery lives entirely on
  the `func templates list` command, designed in §5.5. `func new` is the
  scaffold-one-function verb; `func templates` is the inspect-the-catalog
  verb.
- **No `func templates show <id>` subcommand.** Per-template detail —
  options, prompts, defaults, supported languages — surfaces via
  `func new --template <id> --help` through metadata hydration (§4.5).
  One source of truth, no duplicate rendering path.
- **No `.func/config.json` writes from `func new`.** The command is pure
  scaffold; it never mutates project config. Language is **read** on every
  run from `.func/config.json` `stack.language` (§4.7 / D10) — which
  `func init` writes from its `--language` flag — but `func new` itself
  never writes the file; project config has one author at a time, and that
  author is `func init`.
- **No Java support in v1.** Legacy `func new` hard-rejected Java
  ("use Maven"). vnext gets this for free — Java just won't ship a provider.
- **No worker auto-install.** If the resolved stack workload requires a worker
  or extension bundle that isn't installed, `func new` errors with a pointer
  at `func setup --profile <name>` ([[wcdesign §3.18]]). It does **not**
  install on demand. Same posture for templates content workloads:
  channel-mismatch (§4.8.1) and missing-pkg cases are surfaced as
  `func workload install` hints, not silent installs.
- **No NuGet `extensions.csproj` install for missing bindings** — extension
  bundles cover this in vnext, so the legacy `Metadata.Extensions` install
  path from `CreateFunctionAction` is dropped (see §8 Q2).

## 3. Definitions

| Term | Meaning |
|---|---|
| **Template** | One unit of `func new` output: a set of file payloads + optional binding metadata + an option/prompt schema. Comes from a templates workload's payload. |
| **Template engine** | One of two CLI-internal mechanisms that materialise a template: **DotNet** (shellout `dotnet new <shortName>` against an installed template hive, with catalog + `--help` read from a fully-hydrated `dotnet-templates.json`), or **V2** (`NewTemplate` job/action runner with `UserPrompt[]` inputs and `$(KEY)` substitution; used by **both** Node and Python in v5). The CLI ships both; workloads ship only payloads. The legacy V1 `Files`-map engine is **not** part of vnext — v5 templates workloads ship v2 templates only (templates-workload-spec.md §5.2). |
| **Engine identity** | Determined implicitly by the **directory layout** of the workload payload: `content/v2/` → V2 engine (Node, Python); `content/dotnet-templates.json` at content root → DotNet engine. Never user-visible — no flag, no schema field, no help text. |
| **Template engine provider** | A CLI-internal implementation of `ITemplateEngineProvider` — one **per engine**, not per stack. Two exist: `DotNetEngineProvider` and `V2EngineProvider`. Each knows how to enumerate and apply templates that live under its engine's payload directory. Engine providers are stack-agnostic; they are dispatched against based on Engine identity, not on the project's `StackName`. |
| **Template metadata** | The schema fields that drive option hydration: trigger id, default function name, supported languages, prompts (`UserPrompt[]` for V2; `parameters[]` for DotNet — projected at workload pack time from `template.json` + `dotnetcli.host.json`, templates-workload-spec.md §5.3.1). **No `programmingModel` field, no engine field** — workload publishers ship only current-generation templates (§8 Q-Eng-B). |
| **Provider options** | Per-template `Option<T>` instances surfaced via a hydration helper that reads `FunctionTemplateInfo.UserPrompts` and emits options into an `IInitOptionRegistry`. Visible only under `func new --template <id> --help` (§4.5). |
| **Two-stage parse** | The pattern `func new` uses to expose template-specific options through native `--help`: stage A parses built-ins to discover `--template`; stage B re-parses argv with the template-hydrated options attached. |
| **Active stack** | The stack pinned in `.func/config.json` (`StackOptions.Runtime`) — read once at command entry by `IFunctionsProjectResolver` ([[wcdesign §3.10]]). |
| **NewContext** | The typed input passed to an engine provider — template id (resolved against the catalog), function name, working dir, and the resolved language already settled. |
| **Resolved language** | The canonical language id selected for this `func new` invocation, **read via DI from `IOptionsMonitor<StackOptions>.Get(workingDirectory.FullName)`** — never by re-reading `.func/config.json` directly. `StackOptionsSetup` (registered as `IConfigureNamedOptions<StackOptions>` in `CliHostFactory`) binds the project's `stack.language` (written by `func init`) into the snapshot at host startup. For single-language stacks (Python, PowerShell, Java, Custom, Go) `stack.language` is **omitted** in the file by `InitCommand` (PR #5125) and the CLI substitutes the stack's canonical single language. For multi-language stacks (DotNet C#/F#, Node JS/TS) a missing `stack.language` is a hard error pointing at `func init`. |
| **Active profile** | The profile resolved by `IProfileResolver` ([[wcdesign §3.17]]) at command entry — from `.func/config.json` `defaultProfile`, user prefs, or the built-in default. `func new` and `func templates list` do not accept a `--profile` override (D11); profile is a project-level pin set by `func profile set`. |
| **Templates content workload** | A workload package whose payload is template content (file bodies + metadata) for a single stack. Distinct from a *stack workload*, which contributes a provider (code). One stack typically has both — code and content — installed independently. Detailed in `templates-workload-spec.md`. |
| **Templates channel** | `stable` / `preview` / `experimental`, encoded as the templates workload pkg version's prerelease label (templates-workload-spec.md §4.4.2). At `func new` time the channel is derived from the project's `host.json` `extensionBundle.id` — `func new` does not accept an explicit channel flag. Applies to Node/Python only; DotNet has no channel axis. |
| **`minBundleVersion`** | NuGet version range a Node/Python templates workload pkg declares (in `content/templates-workload.json`) as the lowest extension-bundle version it is compatible with (templates-workload-spec.md §4.4.4). `func new` enforces this against the project's resolved bundle version (§4.8.2); a miss is a hard error in v1, not a warning. |

## 4. Architecture

### 4.1 Component map

```
Func                 src/Func/                                 CLI executable
 ├── Commands/NewCommand.cs                                    built-in command
 ├── Templates/                                                orchestrator
 │     NewCommandRunner.cs           pipeline driver
 │     ITemplateEngineProviderRegistry.cs                      engine lookup by EngineId
 │     TemplateOptionHydrator.cs     UserPrompt[] / parameters[] → Option<T> (engine-agnostic)
 │     TemplatePicker.cs             interactive picker over IInteractionService
 │     NewCommandRenderer.cs         user-facing output
 ├── Templates.V2/                   ──► separate csproj       v2 engine (Node + Python)
 │     V2TemplateEngine.cs           job/action DSL runner with $(KEY) substitution
 │     V2EngineProvider.cs           ITemplateEngineProvider impl (EngineId = "v2")
 │     (engine zone: workload payload's content/v2/)
 └── Templates.DotNet/               ──► separate csproj       dotnet engine
       DotNetTemplateEngine.cs       dotnet-templates.json reader (catalog/help)
                                     + IDotnetCliRunner shell-out (apply)
       DotNetEngineProvider.cs       ITemplateEngineProvider impl (EngineId = "dotnet")
       (engine zone: workload payload's content/dotnet-templates.json
        at content root; hive is provisioned at workload install time
        from content/source.json — templates-workload-spec.md §5.3, §6.3)

Abstractions         src/Abstractions/                         NuGet contract
 └── Templates/                                                new namespace
       ITemplateEngineProvider.cs                              engine-shaped provider
       FunctionTemplateInfo.cs                                 surfaces a template
                                                               (carries Stack + Language + EngineId)
       NewContext.cs                                           engine input
       TemplateApplicationResult.cs                            sealed result hierarchy

Workloads            src/Workloads/                            runtime-loaded
 └── Templates/<stack>/                                        templates content workload
       content/
         v2/templates/templates.json    → V2 engine zone (NewTemplate jobs/actions; Node, Python)
         v2/bindings/userPrompts.json   → shared prompt catalog (V2 ParamId references)
         v2/resources/Resources*.json   → i18n strings
         dotnet-templates.json          → DotNet engine zone (catalog + parameters[] index;
                                          hydrated at workload pack time — templates-workload-spec.md §5.3.1)
         source.json                    → DotNet NuGet pin (hive provisioned at workload install time)
         templates-workload.json        → CLI-owned sibling manifest (minBundleVersion; Node/Python only)
       workload.json                    (kind: content; no entry-point assembly)
```

**Rule:** the CLI references `Templates.V2` and `Templates.DotNet` (project
references). Templates content workloads ship payload only — no code, no DLL
plugin contract. Engine selection is based on the payload's directory layout
(see §4.3, Q-Eng-A); the CLI never asks a workload "which engine are you?"

**Decision:** `Templates.V2` and `Templates.DotNet` are CLI-internal projects,
not NuGet packages — shipped *inside* the `func` binary. The split exists
purely for testability and to keep each engine's quirks (V2's action-runner +
`$(KEY)` substitution, DotNet's `dotnet new` shell-out + JSON catalog) from
leaking into one another. Language resolution is **orthogonal** to engine
choice and is read from `.func/config.json` `stack.language` — there is no
language-detection seam (§4.7). (See §8 Q3 on whether the two engines are
separate assemblies or a single internal assembly with namespaces; D9
supersedes D7's earlier "no separate DotNet engine" stance.)

### 4.2 Contribution points — `ITemplateEngineProvider`

One seam. Engine providers know how to enumerate and apply templates for one
**engine** (V2, DotNet) — they're stack-agnostic, CLI-internal, and
registered by the engine projects themselves (`Templates.V2` /
`Templates.DotNet`), not by workloads. Top-level `func new --help` is
template-agnostic; hydrated options only appear under
`func new --template <id> --help` (hydration is engine-agnostic, see §4.5).
Language resolution is **not** a contribution point — `func new` reads
`stack.language` from `.func/config.json` (`StackOptions.Language`) and
substitutes the stack's canonical single language for single-language stacks
(§4.7).

```csharp
namespace Azure.Functions.Cli.Templates;

public interface ITemplateEngineProvider
{
    /// <summary>
    /// Stable engine identifier ("v2", "dotnet"). The orchestrator
    /// dispatches to a provider by matching this against the engine zone
    /// derived from a template's payload directory (see §4.3, Q-Eng-A). Not
    /// user-visible — never appears in flags, help text, or schema fields.
    /// </summary>
    string EngineId { get; }

    /// <summary>
    /// Enumerates every template this engine can scaffold from the catalog
    /// snapshots the orchestrator has produced from installed templates
    /// content workloads. The provider only sees templates whose payload
    /// directory matches its EngineId. Used for the interactive picker and
    /// by <c>func templates list</c>.
    /// </summary>
    Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        TemplateListContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders + writes the chosen template into the working directory.
    /// </summary>
    Task<TemplateApplicationResult> ApplyAsync(
        NewContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken);
}
```

Supporting records:

```csharp
public sealed record TemplateListContext(
    WorkingDirectory WorkingDirectory,
    string Stack,                         // active stack — narrows the catalog
    string? Language);                    // resolved from .func/config.json stack.language (§4.7)

public sealed record FunctionTemplateInfo(
    string Id,                            // template id (e.g. "HttpTrigger")
    string Stack,                         // owning stack (e.g. "node")
    string EngineId,                      // "v2" | "dotnet" — dispatch key
    string DisplayName,                   // "HTTP trigger"
    string TriggerKind,                   // "http" | "timer" | "queue" | ...
    string? DefaultFunctionName,
    IReadOnlyList<string> Languages,      // which stack languages this template applies to
    TemplateMetadata Metadata);           // schema-driven (see §4.5)

public sealed record NewContext(
    WorkingDirectory WorkingDirectory,
    FunctionTemplateInfo Template,        // resolved by NewCommand before calling
    string FunctionName,
    string? Language,
    bool Force);

public abstract record TemplateApplicationResult
{
    private TemplateApplicationResult() { }
    public sealed record Created(IReadOnlyList<string> Files) : TemplateApplicationResult;
    public sealed record AlreadyExists(IReadOnlyList<string> ExistingFiles) : TemplateApplicationResult;
    public sealed record Failed(TemplateApplicationFailure Failure) : TemplateApplicationResult;
}
```

`TemplateApplicationFailure` follows the sealed-hierarchy idiom ([[wcdesign §3.14]]):
`NoTemplatesWorkloadForChannel(stack, channel, suggestedPackageId, suggestedVersion)`,
`MissingExtensionBundle(stack, suggestedBundleId)`,
`MinBundleVersionTooOld(installedBundleVersion, requiredRange, templatesWorkloadVersion)`,
`WriteFailed`, `InvalidPrompt`, `ProviderError(message, innerException)`.

The first three failure types correspond to the templates-workload-spec's
channel-matching (§4.4.2) and `minBundleVersion` (§4.4.4) enforcement — see
§4.8 below for the resolution + enforcement algorithm, and §5.5.3 / §7 for
the user-facing error UX. The legacy `InvalidProgrammingModel` failure is
dropped — there is no programming-model concept on vnext templates (D9 /
Q-Eng-B).

**Note: `FunctionTemplateInfo.ProgrammingModel` is gone.** Per Q-Eng-B,
workload publishers ship only current-generation templates; there's no
runtime model field to expose on the record. `TemplateListContext` loses its
`ProgrammingModelHint` for the same reason.

**Note: option hydration is no longer on the provider.** The legacy
`GetTemplateOptions(FunctionTemplateInfo, IInitOptionRegistry)` method is
gone; per-template option hydration is engine-agnostic and lives in a shared
`TemplateOptionHydrator` (in `src/Func/Templates/`) that reads
`FunctionTemplateInfo.Metadata.UserPrompts` directly. The result is the same
real `Option<T>` instances under `func new --template <id> --help` (D5);
hydration just doesn't depend on the engine. See §4.5.

**Registration.** Engine providers register from their own `Templates.V2` /
`Templates.DotNet` projects, bare:
`services.AddSingleton<ITemplateEngineProvider, V2EngineProvider>()` (and one
for DotNet). The orchestrator asks `IEnumerable<ITemplateEngineProvider>`
and routes by EngineId. Engine providers don't need tagging — they're
CLI-internal.

### 4.3 Templating engine isolation (v2 / dotnet projects)

vnext has two distinct templating engines. They share **no code** with one
another — that's by construction, not accident. Each lives in its own
CLI-internal csproj, exposes exactly one `ITemplateEngineProvider`, and is
dispatched against by engine identity, never by stack:

| Project | Engine zone (workload payload dir) | Engine mechanism | Used by templates from |
|---|---|---|---|
| `Templates.V2` | `content/v2/` (with `templates/templates.json` + `bindings/userPrompts.json` + `resources/Resources*.json`) | Job/action DSL runner (`NewTemplate.Jobs[].Actions[]`); `$(KEY)` substitution against a variables dict; per-template files materialised from the inline `files: { "<name>": "<contents>" }` map (templates-workload-spec.md §5.2); layout encoded entirely in `TemplateAction.FilePath` — engine is layout-agnostic | Node and Python templates content workloads (templates-workload-spec.md §5.2) |
| `Templates.DotNet` | `content/dotnet-templates.json` at content root, with `content/source.json` sibling (NuGet pin); the dotnet template hive is provisioned at `func workload install` time from `source.json` into a CLI-managed location (templates-workload-spec.md §5.3, §6.3) — **not** packed into the workload payload | Fully-hydrated declarative catalog + per-template `parameters[]` read directly from `dotnet-templates.json` for `func templates list` and `func new --template <id> --help` (offline, deterministic — no `dotnet new` invocation, no NuGet I/O on read paths); **shell-out** via `IDotnetCliRunner` (`dotnet new <shortName> …`) against the provisioned hive only for the actual apply step (D9 / Q-Eng-D) | .NET in-proc + isolated templates content workloads |

**Engine identity is implicit** (D9 / Q-Eng-A). The orchestrator picks an
engine provider by inspecting the workload payload's directory layout:
presence of `content/v2/` → V2 engine; presence of `content/dotnet-templates.json`
at the content root → DotNet engine. A given templates workload ships
exactly one engine zone, so there is no tiebreak — Node/Python ship `v2/`
only, DotNet ships `dotnet-templates.json` only.

**Engine identity is never user-visible.** No flag, no schema field, no help
text mentions "engine". Users see templates by stack + name; the engine
behind a template is CLI-internal dispatch.

**No more programming-model knob.** Per D9 / Q-Eng-B, vnext drops every
`programmingModel` axis: no `--programming-model` flag, no
`FunctionTemplateInfo.ProgrammingModel` field, no `func templates list`
grouping by model. Workload publishers curate to ship only
current-generation templates; "latest" is a publisher responsibility, not a
runtime concept. (Main's `IsNewPythonProgrammingModel()` and
`IsNewNodeJsProgrammingModel()` sniffs are dropped.)

**Read vs scaffold paths (DotNet).** `func templates list` and
`func new --template <id> --help` source catalog rows **and** prompt
definitions directly from the installed templates content workload's
`dotnet-templates.json`, which the workload pack target hydrated at build
time from each upstream `template.json` (+ `dotnetcli.host.json`)
(templates-workload-spec.md §5.3.1). No `IDotnetCliRunner` invocation, no
template-hive read on these paths. Only `provider.ApplyAsync` (the actual
scaffold step in §6) shells out via `IDotnetCliRunner` against the dotnet
template hive that `func workload install` provisioned from `source.json`.
The result: both discovery and help are offline-deterministic from the
workload payload, with **no NuGet I/O at `func new` time**. Workloads stay
content-only — no DLL plugin contract.

```
                NewCommand (Func/Commands/)
                       │
                       ▼
            NewCommandRunner (Func/Templates/)
              1. resolves stack (project / --runtime)
              2. resolves language (read .func/config.json stack.language
                 via StackOptions; fall back to stack's single language
                 if omitted — §4.7)
              3. enumerates templates from installed templates content
                 workloads, grouped by EngineId derived from payload dir
              4. user picks a template
              5. dispatches to engine provider by template.EngineId
                       │
                ┌──────┴────────┐
                ▼               ▼
          V2EngineProvider  DotNetEngineProvider
          (Templates.V2)    (Templates.DotNet)
                │               │
                ▼               ▼
           runs Jobs[].     reads dotnet-
           Actions[] with   templates.json
           $(KEY) subst;    for catalog +
           writes per       parameters[];
           action.FilePath  shells out
           from inline      `dotnet new <sn>
           files map        --<param> <value>`
                            against hive
                            provisioned at
                            workload install
```

### 4.4 Project resolution (gate)

`NewCommand` calls `IFunctionsProjectResolver.ResolveProjectAsync`
([[wcdesign §3.10]]) **before** anything else. Outcomes:

| Resolver result | `func new` action |
|---|---|
| `Created(project)` | Continue. `project.StackName` selects the templates content workload(s); each template's engine zone selects the engine provider (§4.3). |
| `NotCreated(NoWorkloadsInstalled)` | Render hint → exit 1. |
| `NotCreated(NoMatchingFactory)` | "Not a Functions project. Run `func init` first." → exit 1. |
| `Failed(InvalidProject)` | Surface the factory's failure message → exit 1. |

This kills the legacy "auto-init if no `local.settings.json`" branch. Init is
the user's explicit step now ([[wcdesign §3.13]] — post-#5057 init is stricter).

### 4.5 Template metadata hydrates `func new --template <X>` options

A template's metadata schema declares every input the user can provide. The
orchestrator hydrates each one as a typed `Option<T>` and attaches it to a
**per-template parse pass**, so:

- `func new --help` shows only **built-in options** (§5.2). Top-level help is
  template-agnostic.
- `func new --template HttpTrigger --help` shows built-ins **plus** the
  HttpTrigger-hydrated options (`--auth-level`, `--methods`, `--route`, …).
- Every per-template input is a real `Option<T>` — visible in `--help`,
  autocompletion-friendly, validated by the parser. There is no
  prompt-vs-option distinction in the CLI surface: from the user's view a
  prompt **is** an option.

A shared, engine-agnostic helper supplies the hydrated set:

```csharp
namespace Azure.Functions.Cli.Templates;

internal sealed class TemplateOptionHydrator(IInitOptionRegistry registry)
{
    public IReadOnlyList<Option> Hydrate(FunctionTemplateInfo template);
}
```

The hydrator reads `FunctionTemplateInfo.Metadata.UserPrompts` directly —
the engine that produced the template (V2 or DotNet) doesn't participate.
By the time the orchestrator hands the template to the hydrator, every
engine has already projected its prompts into the unified `UserPrompts`
shape on the metadata (V2's `UserPromptAction.Inputs` and DotNet's
`parameters[]` both reach the same schema; see §3 Definitions / Template
metadata).

Hydration rules:

| Metadata field | Maps to |
|---|---|
| V2 prompt `id`, or DotNet `parameters[].name` | Option name, kebab-cased (`authLevel` → `--auth-level`) |
| V2 prompt `label`, or DotNet `parameters[].description` (or `displayName`) | `Option.Description` |
| `defaultValue` | `Option.DefaultValueFactory` |
| V2 prompt `enum`, or DotNet `parameters[].choices[].value` | `Option.AcceptOnlyFromAmong(...)` |
| V2 prompt `validator` regex | Custom parser / `Validators` |
| Missing `defaultValue` + `isRequired` (DotNet) or no default (V2) | Required option (parser errors if not supplied and `--non-interactive`) |
| DotNet `shortNameOverride` / `longNameOverride` (from `dotnetcli.host.json`) | Short / long alias on the same `Option<T>` |

For DotNet templates, every field above is already projected at workload
build time into `dotnet-templates.json` (templates-workload-spec.md §5.3.1) —
so `TemplateOptionHydrator.Hydrate` is a pure in-memory transform over
already-parsed records, with no NuGet I/O or template-hive read.

**DotNet `groupIdentity` dedup across language variants.**
A single Functions item-template (e.g. HttpTrigger) typically ships **two
records** in the upstream NuGet pkg — one C# and one F# — each with the
same `shortName` but distinct `language` and `identity`. The hydrator
treats `dotnet-templates.json` `parameters[]` as per-language; the
catalog-display path (§5.5.2) dedupes adjacent records sharing
`groupIdentity` (templates-workload-spec.md §5.3.1) so the user sees one
"HttpTrigger" row even when both records are installed. The resolved
language from §4.7 then picks the right record at scaffold time. F# users
on a stack that ships F#-capable templates see the F# record's
`parameters[]`; C# users see the C# record's.

**Localization (v1: en-US only).** Per templates-workload-spec.md §5.3.1,
v1 of the DotNet hydration pipeline emits en-US `description` /
`choices[].description` only. The Node/Python `v2/resources/Resources*.json`
sibling files (templates-workload-spec.md §5.2) ship locale variants, but
v1 of `func new --template <id> --help` reads only the en-US default. A
future locale axis can be added without changing the hydration contract —
the schema reserves a `localizations` key.

**Example — HTTP trigger.**

```text
template.Metadata.userPrompts = [
  { id: "authLevel", label: "...", enum: ["function","anonymous","admin"], default: "function" },
  { id: "methods",   label: "...", default: "get,post" },
  { id: "route",     label: "..." /* optional */ }
]

  ── hydrated to ──►

new Option<AuthLevel>("--auth-level")
  { Description = "...", DefaultValueFactory = _ => AuthLevel.Function }
  .AcceptOnlyFromAmong("function", "anonymous", "admin")

new Option<string>("--methods")
  { Description = "...", DefaultValueFactory = _ => "get,post" }

new Option<string?>("--route")
  { Description = "..." }   // optional, no default
```

User invocation is then native System.CommandLine:

```
func new --template HttpTrigger --name MyFn --auth-level anonymous --route api/v1/foo
```

**Two-stage parse.** Because the option set depends on `--template`, the
orchestrator parses in two phases (§6 steps 7–9):

1. **Stage A — discover the template.** Parse with the built-in options only
   (which include `--template`). Extract `--template` value.
2. **Stage B — hydrate + re-parse.** Resolve the template against the
   listed catalog (§6 step 7), call `TemplateOptionHydrator.Hydrate(template)`,
   attach the hydrated options to a second parser, re-parse the original
   argv. Stage-B `--help` shows the union.

`dotnet new <name>` uses the same two-stage trick; the System.CommandLine
pattern is well-trodden.

**No `-p key=value`.** Earlier drafts proposed a single repeatable
`--prop key=value` option as a fallback for un-hydrated prompts. Resolved
(2026-05-27): every prompt hydrates as a real `Option<T>`. There is no
catch-all key/value bag. If a template needs an input, it appears as a
first-class option.

### 4.6 Profile integration

Before resolving a provider, `NewCommand` calls
`IProfileResolver.ResolveAsync` ([[wcdesign §3.17]]). If the resolved profile
has `SupportedRuntimes`, the chosen provider's `Stack` must appear in that
list. Otherwise, render:

```
The active profile `<name>` doesn't support runtime `<stack>`.
Supported runtimes: <comma-separated list>.
Run `func profile set <other-profile>` or `func profile list` to choose another.
```

→ exit 1.

`func new` does **not** accept a `--profile` override or `--offline` flag (D11).
The active profile is resolved silently via the precedence chain above;
profile selection is a project-level pin set by `func profile set`, not a
per-scaffold decision. Profile-source refresh (the `--offline` axis on
`func run`) is irrelevant at scaffold time — the resolved profile is already
in hand, and no scaffold output depends on a fresh remote refresh.

### 4.7 Language resolution

A stack can support multiple languages (dotnet: C#/F#; node: JS/TS). `func new`
derives the language by **consuming `IOptionsMonitor<StackOptions>` from DI**
and calling `.Get(workingDirectory.FullName)`. That snapshot is populated by
`StackOptionsSetup` (`IConfigureNamedOptions<StackOptions>`, registered in
`CliHostFactory`) at the time the snapshot is first requested for that
directory — `StackOptionsSetup` is the **only** code path that reads
`.func/config.json` for the `stack` section, and it does so via `IConfiguration`
binding. **`func new` never opens `.func/config.json` itself.** This mirrors
the established pattern for project-scoped config consumers
(`ProfileResolver`, `SetupRunner`, `ProfileListCommand` all inject
`IOptionsMonitor<ProjectProfileOptions>` and read via `.Get(projectDirectory)`).

There is **no `--language` flag on `func new`, no interactive prompt, no
filesystem fingerprinting**: `func init` already settled the language and
persisted it in `.func/config.json`; the options pipeline has already
materialised it into `StackOptions.Language`; `func new` is a plain reader
of that snapshot (D10).

`InitCommand` writes the key conditionally
(`src/Func/Commands/InitCommand.cs:266`):

```csharp
if (!string.IsNullOrWhiteSpace(language))
{
    stackConfig[CliConfigurationNames.StackLanguageKey] = language;
}
```

and **omits** `stack.language` when the stack only supports one language
(PR #5125 — see Constants `CliConfigurationNames.StackLanguageKey = "language"`).

**Injection shape** (concrete, mirroring `ProfileResolver`):

```csharp
internal sealed class NewCommandRunner(
    IInteractionService interaction,
    IFunctionsProjectResolver projectResolver,
    IProfileResolver profileResolver,
    IOptionsMonitor<StackOptions> stackOptions,           // ← DI-bound; no file read here
    ITemplateEngineProviderRegistry engineProviders,
    /* … */)
{
    public async Task<int> ExecuteAsync(NewInvocation invocation, CancellationToken ct)
    {
        // … step 5 (Language resolution):
        string projectDirectory = Path.GetFullPath(invocation.WorkingDirectory.FullName);
        StackOptions stack = stackOptions.Get(projectDirectory);
        string? resolvedLanguage = ResolveLanguage(stack, activeStackId, supportedLanguages);
        // …
    }
}
```

**Resolution rules** (single read of the bound options snapshot, no fallback chain):

1. Read `StackOptions.Language` via
   `IOptionsMonitor<StackOptions>.Get(workingDirectory.FullName)` (DI-bound;
   no file I/O at this step).
2. If non-null/non-empty → use it.
3. If null/empty **and** the active stack is single-language (per the
   `IProjectInitializer.SupportedLanguages` list for the active stack) →
   substitute that single language. This mirrors `InitCommand`'s
   omit-on-single-language behaviour (PR #5125).
4. If null/empty **and** the active stack is multi-language → hard error.
   The project is multi-language but `stack.language` is missing. Exit 1:

   ```
   Cannot determine language for stack '<stack>' in '<path>'.
   `stack.language` is missing from .func/config.json — run 'func init'
   to scaffold the project (which writes the language) before adding
   functions.
   ```

**Per-stack expected values** (canonical ids — match what `InitCommand` /
`IProjectInitializer.SupportedLanguageAliases` accept):

| Stack | Multi-language? | `stack.language` values written by `func init` |
|---|---|---|
| dotnet | yes | `csharp` / `fsharp` (omitted only if the workload genuinely supports one language at install time) |
| node | yes | `javascript` / `typescript` |
| python | no — single-language | omitted by `InitCommand`; CLI substitutes `python` |
| powershell | no | omitted; CLI substitutes `powershell` |
| java | no | omitted; CLI substitutes `java` |
| custom | no | omitted; CLI substitutes `custom` |
| go (future) | no | omitted; CLI substitutes `go` |

**Why options-binding instead of file-read or init-artifact fingerprinting
(revisits D10).**
The init contract — `func init --language typescript` for a Node-TS
project — already persists the user's chosen language in
`.func/config.json` via `InitCommand.WriteCliConfigurationFile`. The CLI's
existing `IConfiguration` pipeline + `StackOptionsSetup` already binds that
value into `StackOptions.Language`. Consuming the bound snapshot via
`IOptionsMonitor<StackOptions>.Get(...)`:

- **Avoids opening a file we've already read.** The DI options pipeline
  is the single read path; per-call file I/O would duplicate work and
  bypass the options system's caching/diagnostics surface.
- **Matches the codebase convention.** AGENTS.md mandates options binding
  over direct `IConfiguration` reads (let alone direct `File.ReadAllText`);
  `ProfileResolver` / `SetupRunner` / `ProfileListCommand` already inject
  `IOptionsMonitor<ProjectProfileOptions>` to the same effect.
- **Skips the file-existence checks** (`tsconfig.json`, `*.csproj` /
  `*.fsproj`) and their edge cases (mixed-language repos, missing markers,
  contradictory markers — see legacy Q-Lang-2).
- **Skips the `IStackLanguageDetector` per-stack seam** and its
  registration ceremony.
- **Eliminates drift** between what `func init` chose and what `func new`
  re-derives — there's only one read path.

`StackOptions.Language` IS the in-memory view of the persistence layer.
`func new` is a strict reader of that view; it never opens `.func/config.json`
itself.

**No persistence write from `func new`.** Unchanged from §2: `func new`
never mutates `.func/config.json`. The single author of the file is
`func init` (today) or a future `func config` verb. If a user deletes
`.func/config.json` outright, the bound `StackOptions` is empty
(`Runtime` and `Language` both `null`) — for multi-language stacks
`func new` errors with the same hint pointing at `func init`.

### 4.8 Templates workload selection and bundle compatibility

Once the provider is picked (§4.3) and the language is resolved (§4.7),
`func new` selects exactly one **installed templates content workload** to
source the template payload from. Selection is split across two concerns
owned by the templates-workload-spec:

- **Channel match** — pick the templates workload pkg whose prerelease
  label matches the project's bundle channel
  (templates-workload-spec.md §4.4.2).
- **Min-bundle compatibility** — within the channel-matched pkg, verify
  the project's resolved bundle version satisfies the templates workload's
  declared `minBundleVersion`
  (templates-workload-spec.md §4.4.4).

This spec owns *when* the checks fire (pipeline placement and ordering),
*what* the user sees (failure variants and hint text), and *which*
`IExtensionBundleResolver` / templates-registry seams the orchestrator
calls. The data shapes (`workload.json`, sibling
`content/templates-workload.json`, nuspec `minBundle:` tag) live in the
templates-workload-spec.

#### 4.8.1 Channel match (Node and Python)

DotNet templates workloads have **no channel axis** (no extension-bundle
dependency — templates-workload-spec.md §4.4.3); the DotNet path skips this
sub-step. Node and Python perform the following:

1. **Read the project's bundle id.** Parse `host.json`'s
   `extensionBundle.id`. If `extensionBundle` is absent or `id` is
   missing, `func new` errors with a hint pointing at extension-bundle
   configuration (no implicit default).
2. **Map id → templates channel** via the table in
   templates-workload-spec.md §4.4.2:

   | `host.json` `extensionBundle.id` | Templates channel (prerelease label) |
   |---|---|
   | `Microsoft.Azure.Functions.ExtensionBundle` | stable (no label) |
   | `Microsoft.Azure.Functions.ExtensionBundle.Preview` | `-preview` |
   | `Microsoft.Azure.Functions.ExtensionBundle.Experimental` | `-experimental` |

3. **Enumerate installed templates workload pkgs** for the active stack
   (`Azure.Functions.Cli.Workloads.Templates.<stack>`) from the workload
   registry.
4. **Filter to the channel-matched subset** — prerelease label of the pkg
   version must equal the channel from step 2. **No cross-channel
   fallback** (a stable templates pkg does not satisfy an experimental
   project; templates-workload-spec.md §4.4.2).
5. **Pick the highest** version from the channel-matched subset.

If the channel-matched subset is empty, fail with
`NoTemplatesWorkloadForChannel(stack, channel, suggestedPackageId, suggestedVersion)`.
The hint text identifies the specific channel-suffixed pkg the user needs
(see §5.5.3 row "no installed templates workload matches project's bundle
channel"). The hint shape:

```
No installed templates workload matches this project's bundle channel
(`<bundle-id>` → channel `<channel>`).
Install one with:
  func workload install Azure.Functions.Cli.Workloads.Templates.<stack> --version <ver-with-channel-suffix>
Or change `host.json` `extensionBundle.id` if the channel was set in error.
```

> **Pack-time channel pre-filter (Node only).** Per
> templates-workload-spec.md §4.3 / §6.1, the Node templates workload's
> pack pipeline already filters its `templates.json` against the channel's
> authoritative bundle binding set (via HTTP-Range extraction of
> `bin/extensions.json` from the channel's latest listed bundle in
> `index.json`). Templates whose required bindings aren't present in the
> channel are dropped at **pack time**. The catalog the channel-matched
> Node workload carries on disk is therefore already a per-channel
> subset — `func new` does not re-run the binding-availability check at
> invocation time. This is a workload-build guarantee; the
> `minBundleVersion` check in §4.8.2 remains the authoritative runtime
> compatibility gate (it catches the cross-axis case of a project whose
> resolved bundle is older than the templates workload's compatibility
> floor, regardless of binding coverage).

#### 4.8.2 Min-bundle compatibility check (Node and Python)

After channel match succeeds and a single templates workload is selected,
the orchestrator reads its sibling manifest
`content/templates-workload.json` (templates-workload-spec.md §5.2) and
extracts `minBundleVersion` (a NuGet version range, e.g. `[4.18.0, )`).

The orchestrator then asks `IExtensionBundleResolver` ([[wcdesign §3.9]])
for the project's resolved bundle version and compares:

| Comparison result | Behaviour |
|---|---|
| Resolved bundle version ∈ `minBundleVersion` range | Continue. |
| Resolved bundle version ∉ range (too old) | Fail with `MinBundleVersionTooOld(installedBundle, requiredRange, templatesWorkloadVersion)`. Exit 1. |
| `IExtensionBundleResolver` returns `WorkloadMissing` / `EmptyIntersection` (no bundle resolvable at all) | Fail with `MissingExtensionBundle(stack, suggestedBundleId)`. Exit 1. |

The min-bundle check is fail-fast at the pipeline stage in §6, **before**
template hydration and **before** scaffolding. Catching the mismatch
before any I/O keeps the partial-write window small.

> The original templates-workload-spec.md §4.4.4 hedges "warning or
> error." This spec **resolves to error** for v1 — a min-bundle violation
> is exactly the kind of silent-failure-later we want to surface
> immediately. A future `--ignore-min-bundle` opt-out can soften to a
> warning if real users hit false positives.

#### 4.8.3 DotNet templates workload

DotNet's selection path is degenerate:

- **No channel.** Pick the highest installed
  `Azure.Functions.Cli.Workloads.Templates.DotNet` version.
- **No `minBundleVersion`.** DotNet templates have no extension-bundle
  dependency; the check is skipped entirely.
- **Hive provisioning.** §6 step 11b for DotNet reduces to "verify the
  `source.json`-pinned NuGet template pkg is provisioned into the dotnet
  template hive." Provisioning happens at `func workload install` time
  (templates-workload-spec.md §6.3); if the hive is empty (e.g. install
  failed silently), `func new` errors with a re-install hint.

## 5. CLI surface

### 5.1 Command registrations

| Invocation | Notes |
|---|---|
| `func new` | Primary. Built-in command. The only registered verb for the scaffold-one-function flow. |

The legacy `func function new` / `func function create` aliases from v4 are
**not carried forward**. v5 has a single canonical verb; users who type the
old aliases see the standard "unknown command" hint pointing at `func new`
(see §7 Migration).

### 5.2 Options

Built-in (declared on `NewCommand`, visible in top-level `func new --help`):

| Option | Alias | Type | Purpose |
|---|---|---|---|
| `--name` | `-n` | `string?` | Function name. Defaults to template's `DefaultFunctionName`. |
| `--template` | `-t` | `string?` | Template id. If omitted in TTY, interactive picker. |
| `--force` | — | `bool` | Overwrite existing files. |
| `--non-interactive` | — | `bool` | Refuse to prompt; exit 1 if any input is missing. |
| `--output` | — | `enum {plain, json}` | Output mode (mirrors `func setup`). |

Path positional argument (inherited via `AddPathArgument()`): the project
directory. Defaults to cwd. **Note:** `func new` does not "create" a project —
this is just the working directory.

**No legacy positional template shortcut.** v4's `func new HttpTrigger`
(first non-flag token = template id) is not carried forward — see §8 Q7.
The template id is supplied only via `--template <id>` / `-t <id>`. The
positional slot is reserved for the path. Typing the v4 form yields a
standard System.CommandLine parse error pointing the user at `--help`;
the §7 Migration table records the break.

**Template-hydrated options** (visible only under
`func new --template <id> --help`). Produced by the engine-agnostic
`TemplateOptionHydrator.Hydrate(template)` from each template's
`Metadata.UserPrompts` (§4.5). Examples surfaced by the v2 bundle metadata
(Node, Python) and `dotnet-templates.json` (DotNet):

| Option | Provider / template | Source field |
|---|---|---|
| `--auth-level` | HTTP triggers (Node, Python) | `UserPrompt[id=authLevel]` |
| `--methods` | HTTP triggers | `UserPrompt[id=methods]` |
| `--route` | HTTP triggers | `UserPrompt[id=route]` |
| `--schedule` | Timer triggers | `UserPrompt[id=schedule]` |
| `--queue-name`, `--connection` | Queue / Service Bus triggers | `UserPrompt[id=queueName]`, `connection` |
| `--file` | Python v2 templates | Per-template prompt on each v2 entry |
| `--namespace`, `--access-rights` (`--auth-level` alias via `dotnetcli.host.json`) | DotNet HTTP triggers | `dotnet-templates.json` `parameters[name=namespace \| AccessRights]` (templates-workload-spec.md §5.3.1) |

The legacy `--csx` flag is **not** carried forward as a top-level option,
and `.csx` script-mode .NET functions are not scaffoldable from v5 at
all — see §8 Q6 for the full rationale (no isolated-worker `.csx`
programming model; in-process .NET is out of scope per vnext-design
Part IX). Users on `.csx` stay on v4 tooling until the in-process /
script-mode story is revisited.

#### 5.2.1 Available templates section in top-level `--help`

Top-level `func new --help` stays **template-agnostic** for option flags
(§4.5 / §8 Q5) — per-template `Option<T>` instances only appear under
`func new --template <id> --help`. To still give the user a useful "what
can I scaffold?" signal in plain `func new --help`, the help renderer
injects a per-stack **Available templates** section after the built-in
`Options:` block. This section renders catalog rows only — no option flags,
no per-template detail — and shares its data source with
`func templates list` (§5.5), so the two surfaces stay in lockstep.

```
$ func new --help
Description:
  Scaffold a new Azure Function from an installed template.

Usage:
  func new [options] [<path>]

Options:
  -n, --name <name>            ...
  -t, --template <template>    ...
  --force                      ...
  --non-interactive            ...
  --output <plain|json>        ...
  -?, -h, --help               Show help and usage information.

Available templates (stack: python):
  http        HTTP trigger             Function triggered by HTTP requests.
  timer       Timer trigger            Function triggered on a schedule (NCRONTAB).
  queue       Queue trigger            Function triggered by Azure Storage Queue messages.
  blob        Blob trigger             Function triggered by Azure Blob Storage events.
  …
  Run 'func new --template <name> --help' for template-specific options,
  or 'func templates list' for the full catalogue.
```

Rendering rules:

- **Three columns** (short name, display name, description), truncated to
  terminal width with `…`. Same layout as `func templates list` plain
  output (§5.5.2) so the two read identically.
- **Stack header** mirrors §5.5.2. `func new` requires an init'd project
  (§4.4), so the stack is always resolved — there is no stackless rendering
  path on this surface.
- **Tab completion.** `func new --template <TAB>` reuses the same catalog
  as the help section by wiring `Option<string>.CompletionSources` to the
  engine provider registry (§4.1). Same data, same source of truth.
- **Engine-agnostic.** The renderer reads `FunctionTemplateInfo` records
  aggregated across every `ITemplateEngineProvider.ListTemplatesAsync`
  for the active stack; for DotNet that data ultimately comes from
  `dotnet-templates.json`, for Node/Python from the workload-shipped
  `content/v2/templates/templates.json`. The renderer itself doesn't care
  which engine emitted a row.
- **Per-template options remain under `--template <id> --help`.** This
  section is a discovery aid, not an option-flag dump. The reasons
  per-template option hydration cannot work at the top level (option-name
  collisions across templates, help-text explosion, stage-A parser can't
  pick a hydration target before `--template` is bound) are unchanged
  from §4.5 / §8 Q5.

### 5.3 `<trigger> help` special syntax

Legacy: `func new HttpTrigger help` printed trigger-specific help (JS/TS/Python
only). We **drop** that syntax. Per-template options surface via
`func new --template HttpTrigger --help` (built-in `--help` over the
metadata-hydrated options, §4.5) and `func templates list` for the catalogue
(§5.5).

(Captured as a behavioural break vs main; surfaces in §7 Migration.)

### 5.4 Decision tree

```
func new [path]
│
├─ ResolveProfileAsync (.func/config.json defaultProfile → user prefs → built-in)
│
├─ ResolveProjectAsync (IFunctionsProjectResolver)
│    NotCreated      → render hint, exit 1
│    Failed          → render failure, exit 1
│    Created(project) → project.StackName is the active stack
│
├─ ValidateStackAgainstProfile (ResolvedProfile.SupportedRuntimes)
│    mismatch → render hint, exit 1
│
├─ SelectTemplatesWorkload (§4.8)                  (formerly two stages — engine providers are CLI-internal, not stack-keyed)
│    Node/Python:
│      read host.json extensionBundle.id → channel
│        missing → render extension-bundle-config hint, exit 1
│      filter installed Templates.<stack> pkgs by prerelease label == channel
│        empty → NoTemplatesWorkloadForChannel → render install hint, exit 1
│      pick highest version from channel-matched subset
│    DotNet:
│      pick highest installed Templates.DotNet (no channel)
│        none → render install hint, exit 1
│
├─ ResolveLanguage (§4.7)
│    inject IOptionsMonitor<StackOptions>; read .Get(workingDirectory.FullName).Language
│      non-null → use it
│      null && active stack is single-language → substitute stack's only language
│      null && active stack is multi-language → exit 1 ("run 'func init' first;
│                                                       stack.language missing")
│
├─ ListTemplates(selectedWorkload, language)         → aggregate across all engine providers; each template carries its EngineId
│    (no list-and-exit branch — discovery lives on `func templates list`)
│
├─ ResolveTemplate                                    (stage-A parse)
│    --template given → match by Id (case-insensitive)
│    not given        → interactive picker (TemplatePicker) or exit 1 if --non-interactive
│
├─ HydrateTemplateOptions + re-parse                  (stage-B parse, §4.5)
│    TemplateOptionHydrator.Hydrate(template)         (engine-agnostic; reads template.Metadata.UserPrompts)
│    re-parse argv with hydrated options attached
│
├─ ResolveFunctionName
│    --name given → use it
│    not given    → prompt (default = Template.DefaultFunctionName ?? Template.Id)
│
├─ CollectRemainingInputs (interactive fallback)
│    every hydrated option is parsed in stage B;
│    missing required values without defaults → prompt or exit 1 if --non-interactive
│
├─ ValidateBundlePresence (§4.8.2, Node/Python only)
│    IExtensionBundleResolver returns WorkloadMissing/EmptyIntersection
│      → MissingExtensionBundle → render `func setup` hint, exit 1
│
├─ ValidateMinBundleVersion (§4.8.2)
│    Node/Python:
│      read selectedWorkload's content/templates-workload.json
│      resolvedBundleVersion ∉ minBundleVersion range
│        → MinBundleVersionTooOld → render version-mismatch hint, exit 1
│    DotNet:
│      verify source.json-pinned pkg is provisioned into dotnet template hive
│        missing → render re-install hint, exit 1
│
├─ engineProviders[template.EngineId].ApplyAsync(NewContext, ParseResult)
│    Created       → render file list, exit 0
│    AlreadyExists → render "use --force" hint, exit 1
│    Failed        → render typed failure, exit 1
│
└─ (post-deploy hints removed — vnext drops the programming-model concept; see §7)
```

### 5.5 Template discovery — `func templates list`

Decided 2026-05-27: discovery is a **separate top-level command**, not a flag
on `func new`. This matches main's existing surface
(`ListTemplatesAction` registered under `Context.Templates` →
`func templates list`) and keeps `func new` strictly a scaffold-one-function
verb.

`func templates list` is its own built-in command (`TemplatesListCommand`,
sibling to `NewCommand` in `src/Func/Commands/`). It enumerates templates
through the same engine provider registry the orchestrator uses
(`ITemplateEngineProviderRegistry` — see §4.1 and §6 step 6), aggregating
across every engine provider for the active stack so the catalogue
surfaced to the user is always the catalogue `func new` would actually
scaffold from.

#### 5.5.1 Surface

```
func templates list [path]
  --output {plain|json}    output mode (default: plain)
```

No `--stack <id>` override (§5.5.5: `func templates list` requires an
init'd Functions project; the stack is read from `.func/config.json`
`stack.runtime`). No `--template <id>` filter (one-template-at-a-time
isn't a list query — if we add per-template detail later it's a
separate verb). The single positional `[path]` is the project directory
(defaults to cwd), passed through to `IOptionsMonitor<StackOptions>.Get(...)`
exactly like §4.7.

#### 5.5.2 Default output (plain)

```
$ func templates list
Templates for stack: python  (language: Python)

  NAME                     TRIGGER       DESCRIPTION
  HttpTrigger              http          Function triggered by HTTP requests.
  TimerTrigger             timer         Function triggered on a schedule (NCRONTAB).
  QueueTrigger             queue         Function triggered by Azure Storage Queue messages.
  BlobTrigger              blob          Function triggered by Azure Blob Storage events.
  EventHubTrigger          eventhub      Function triggered by Azure Event Hubs events.
  CosmosDBTrigger          cosmosdb      Function triggered by changes in Azure Cosmos DB.
  ServiceBusQueueTrigger   servicebus    Function triggered by Service Bus queue messages.
  ServiceBusTopicTrigger   servicebus    Function triggered by Service Bus topic subscriptions.
  KafkaTrigger             kafka         Function triggered by Kafka messages.

Create one with:
  func new --template <NAME> --name <function-name>
```

Layout rules:
- Three columns: `NAME`, `TRIGGER`, `DESCRIPTION`. `LANGUAGE` lives in the
  header, not per row — when one language is resolved the column is noise.
  For DotNet projects the resolver picks a single language (C# or F#) from
  `stack.language` (§4.7); the catalog renderer dedupes adjacent records
  sharing `dotnet-templates.json` `groupIdentity` so each `HttpTrigger`
  (etc.) shows once even when both C# and F# variant records are installed
  (templates-workload-spec.md §5.3.1).
- `NAME` is the provider-scoped template id (`HttpTrigger`,
  `python.HttpTrigger` — depending on the §8 Q-namespace outcome). For
  Node/Python this is the V2 `NewTemplate.id`; for DotNet it is the
  `dotnet-templates.json` `id` (= `shortNames[0]`).
- `DESCRIPTION` is the template metadata's existing `Description` field
  (already in v2 `NewTemplate[]` — no new schema needed). For the DotNet
  provider, `DESCRIPTION` is the `description` field on each
  `dotnet-templates.json` entry (templates-workload-spec.md §5.3.1), which
  was hydrated at workload build time from the upstream `template.json` —
  same data, no run-time `dotnet new list` shell-out.
  Truncated to terminal width with `…`; full text via `--output json`.
- **Node catalog is pre-filtered per channel at workload pack time**
  (templates-workload-spec.md §6.1). Templates whose required bindings
  aren't satisfied by the channel's authoritative bundle are dropped at
  pack time, so the rows surfaced by `func templates list` /
  `func new --help` for a Node project reflect only templates that should
  scaffold cleanly against the project's resolved bundle. Renderer-side
  hiding is therefore not needed — the workload already shipped the right
  subset.

#### 5.5.3 Empty / error cases

| Condition | Output | Exit |
|---|---|---|
| No `.func/config.json` resolvable at `<path>` (uninitialized directory; §5.5.5) | "`func templates list` needs a Functions project. Run `func init` first to choose a stack and language." | 1 |
| `StackOptions.Runtime` is null/empty (no `stack.runtime` in `.func/config.json`) | "`func templates list` needs a stack pinned in .func/config.json. Run `func init` first." | 1 |
| No template providers registered | "No templates available. Install a templates workload: `func workload install Azure.Functions.Cli.Workloads.Templates.<stack>`." | 1 |
| Provider registered but content workload missing | Per-stack hint pointing at the specific package id. | 1 |
| No installed templates workload matches project's bundle channel (Node/Python, §4.8.1) | "No installed templates workload matches this project's bundle channel (`<bundle-id>` → channel `<channel>`). Install: `func workload install Azure.Functions.Cli.Workloads.Templates.<stack> --version <ver-with-channel-suffix>`. Or change `host.json` `extensionBundle.id` if the channel was set in error." | 1 |
| Selected templates workload's `minBundleVersion` newer than project's resolved bundle (Node/Python, §4.8.2) | "Installed templates workload `<pkg> <ver>` requires extension bundle in range `<range>`, but the project resolves to `<resolvedBundleVersion>`. Update the bundle range in `host.json` or install an older templates workload pkg version." | 1 |
| `StackOptions.Language` is null/empty and the active stack is multi-language (`stack.language` missing from `.func/config.json`) | "Cannot determine language for stack `<stack>` in `<path>`. `stack.language` is missing from .func/config.json — run `func init` to scaffold the project before adding functions." | 1 |
| Provider matches but returns 0 templates after filtering | Print the header + "(no templates match the current filters)" | 0 |

#### 5.5.4 JSON output

`--output json` emits a single envelope (not NDJSON), since `list` is a
finite, ordered query:

```jsonc
{
  "stack": "python",
  "language": "Python",
  "templates": [
    {
      "id": "HttpTrigger",
      "displayName": "HTTP trigger",
      "triggerKind": "http",
      "languages": ["Python"],
      "engineId": "v2",
      "description": "Function triggered by HTTP requests.",
      "defaultFunctionName": "HttpTrigger",
      "requiresExtensionBundle": true,
      "minBundleVersion": "4.0.0",
      "options": [
        { "name": "--auth-level", "type": "enum", "values": ["function","anonymous","admin"], "default": "function" }
      ],
      "prompts": [ /* metadata-derived, see §4.5 */ ]
    }
  ]
}
```

Tooling (IDE plugins, autocompletion, `gh`-style `--format` consumers) reads
JSON. The plain output stays catalog-only (no per-template detail block);
template options live on `func new --template <id> --help` (§4.5).

**DotNet entries** carry additional fields from `dotnet-templates.json`
(templates-workload-spec.md §5.3.1) so tooling can dedupe and dispatch
without re-parsing the workload payload: `identity` (stable id used by
the upstream dotnet template engine), `groupIdentity` (shared across
C#/F# variants of the same template — tooling that wants a single row
per HttpTrigger collapses on this), `classifications`, and
`shortNames[]`. Node/Python entries do not carry these fields.

#### 5.5.5 Resolution gates

`func templates list` **requires an init'd Functions project**
(`.func/config.json` resolvable from `[path]`, with at minimum
`stack.runtime` set). The stack is read from `StackOptions.Runtime` via
the same `IOptionsMonitor<StackOptions>.Get(workingDirectory.FullName)`
path §4.7 uses for language. Language follows the same rules as §4.7:
read `StackOptions.Language`, substitute the stack's canonical
single-language constant when the key is omitted (PR #5125), hard-error
on multi-language stacks when missing. For Node/Python the templates
workload is selected via the channel-match algorithm in §4.8.1 against
`host.json` `extensionBundle.id`; for DotNet the highest installed
`Workloads.Templates.DotNet` pkg is picked (§4.8.3). The catalogue
surfaced to the user is therefore always the catalogue `func new` would
actually scaffold from.

**No stackless run, no `--stack` override.** Three reasons:
(a) Channel resolution requires `host.json` `extensionBundle.id`
(§4.8.1); without a project there is no bundle id and no defined
channel-selection algorithm.
(b) Min-bundle compatibility (§4.8.2) cannot be evaluated without a
resolved bundle version.
(c) `func new` itself requires a project (§4.4); a discovery surface
that lists templates the user cannot then scaffold is misleading.
Users browsing "what templates exist for stack X" before initialising a
project should use `func quickstart list` (a separate command that
ships its own catalogue, has no per-project channel concept, and
explicitly targets the "evaluating which stack to pick" UX).

If `[path]` does not resolve to an init'd project, `func templates list`
exits 1 with the standard hint pointing at `func init` (see §5.5.3
"No `.func/config.json` resolvable…" row). This matches `func new`'s
project-gate posture (§4.4) so the two commands fail in the same way for
the same reason.

> This **revises** an earlier draft (and v4's `ListTemplatesAction`,
> which ran in any directory). v4 had no channel concept; v5 does, and
> "show a stackless catalogue" has no coherent algorithm without a
> project bundle. The constraint is intentional; see §8 Q11 for the
> rationale and §7 for the migration note.

## 6. Orchestration pipeline (CLI-side)

```
NewCommandRunner.ExecuteAsync
 │
 ├── 1. ResolveProfile             (IProfileResolver)
 ├── 2. ResolveProject             (IFunctionsProjectResolver)
 ├── 3. ValidateStack              (against ResolvedProfile.SupportedRuntimes)
 ├── 4. SelectTemplatesWorkload    (§4.8: channel match for Node/Python; degenerate for DotNet)
 │        Fail: NoTemplatesWorkloadForChannel(stack, channel, …)
 ├── 5. ResolveLanguage            (§4.7: inject IOptionsMonitor<StackOptions>; read .Get(workingDirectory.FullName).Language — DI-bound, no file I/O; for single-language stacks fall back to the stack's canonical language when omitted; for multi-language stacks a missing value → exit 1 with "run func init first")
 ├── 6. ListTemplates              (aggregate across every ITemplateEngineProvider for the active stack, scoped to step-4 workload) — filtered by language
 ├── 7. ResolveTemplate            (stage-A parse: --template or interactive picker)
 ├── 8. HydrateTemplateOptions     (TemplateOptionHydrator.Hydrate(template) — engine-agnostic)
 ├── 9. ReParseWithHydration       (stage-B parse: built-ins + hydrated options against original argv)
 ├── 10. ResolveFunctionName       (stage-B --name, then prompt fallback)
 ├── 11a. ValidateBundlePresence   (IExtensionBundleResolver: bundle resolvable?)
 │         Fail: MissingExtensionBundle(stack, suggestedBundleId)
 ├── 11b. ValidateMinBundleVersion (§4.8.2: resolved bundle version ∈ workload's minBundleVersion range?)
 │         Fail: MinBundleVersionTooOld(installedBundle, requiredRange, templatesWorkloadVersion)
 │         (DotNet: skipped — no minBundleVersion declared. DotNet substitutes a hive-provisioning check.)
 ├── 12. ApplyAsync                (dispatch by template.EngineId → engineProviders[EngineId].ApplyAsync)
 └── 13. Render result             (NewCommandRenderer; honours --output)
```

Steps 1–5 reuse existing CLI services. Step **4** is the
templates-workload selection stage detailed in §4.8 — for Node/Python it
maps `host.json` `extensionBundle.id` → templates channel and picks the
highest installed templates pkg whose prerelease label matches, with no
cross-channel fallback; for DotNet it picks the highest installed
`Azure.Functions.Cli.Workloads.Templates.DotNet` (no channel axis). The
selected workload is what step 6 lists templates from — so subsequent
steps operate on a single, fixed templates payload. Step **5** runs the
language-resolution rule documented in §4.7: inject
`IOptionsMonitor<StackOptions>` from DI and call
`.Get(workingDirectory.FullName).Language`. `StackOptionsSetup` is the
single code path that opens `.func/config.json` for the `stack` section;
`func new` consumes the bound snapshot and never re-reads the file. When
the key is present, use it; when omitted on a single-language stack, the
CLI substitutes the stack's canonical language (PR #5125 — `InitCommand`
intentionally omits `stack.language` for single-language stacks). When
omitted on a multi-language stack (dotnet, node) it is a hard error
pointing at `func init`. No prompt, no `--language` flag, no filesystem
fingerprinting: the options pipeline is the single source of truth (D10).

Steps **7–9 are the new core**:
two-stage parsing. Stage A parses the built-in option set only (which is
enough to discover `--template`); stage B asks the engine-agnostic
`TemplateOptionHydrator` to hydrate per-template options from
`template.Metadata.UserPrompts` and re-parses the original argv with the
union. `dotnet new <name>` uses the same pattern. Step **12** then
dispatches by `template.EngineId` to the matching engine provider's
`ApplyAsync` — V2 runs the job/action DSL writing per-action `FilePath`
from the inline `files` map; DotNet shells out via `IDotnetCliRunner`
against the hive provisioned at workload install time from `source.json`
(templates-workload-spec.md §6.3).

Step **11a** reuses `IExtensionBundleResolver` ([[wcdesign §3.9]]) when the
template declares a binding that needs an extension bundle. If the resolver
returns `WorkloadMissing` / `EmptyIntersection`, the new-command emits
`MissingExtensionBundle` with a hint pointing at `func setup --profile <name>`.

Step **11b** runs the **min-bundle enforcement** described in §4.8.2 against
the templates workload selected in step 4b: read its sibling manifest
`content/templates-workload.json`, extract `minBundleVersion`, and verify
the project's resolved bundle version satisfies the range. A miss surfaces
`MinBundleVersionTooOld` with the installed-version / required-range /
templates-pkg-version triple so the user can choose between updating
`host.json` or installing a different templates workload pkg version
(§5.5.3). For DotNet, 11b is replaced by a hive-presence check: the
`source.json`-pinned NuGet pkg must be provisioned in the dotnet template
hive — failure points the user at re-running `func workload install`
(templates-workload-spec.md §6.3).

Both step-4 and step-11b run **before** any I/O against the user's
project directory. Catching channel and min-bundle mismatches early keeps
the partial-write window empty: if `func new` exits non-zero before step
12 (engine dispatch / `ApplyAsync`), no files have been written or modified.

**Interactive fallback.** A required hydrated option (no default, not supplied
on argv) prompts via `IInteractionService` when `--non-interactive` is not set;
otherwise the parser errors with `--<name> is required`. There's no separate
"prompt loop" — every prompt is an `Option<T>`, and the parser is the only
interaction surface.

## 7. Compatibility / migration

| Legacy (main) | vnext `func new` | Reason |
|---|---|---|
| Auto-runs `func init` if not initialised | Errors with hint to run `func init` first | #5057 made init stricter; auto-init hides state. |
| Hard-rejects Java with "use Maven" | No provider registered → generic "no template provider" hint | Java provider can be added without changing the orchestrator. |
| `--csx` forces legacy CSX path | Dropped (see §8 Q6). `.csx` script-mode functions are not scaffoldable on v5. | No isolated-worker `.csx` model; in-process .NET is out of scope per vnext-design Part IX item 9. |
| `func new <trigger> help` (positional help) | Dropped — use `func new --template <id> --help` | Two-stage parse exposes every per-template option to native `--help`. |
| `func new <template>` (positional template id as first non-flag token) | Dropped (see §8 Q7). Template id must be supplied via `--template <id>` / `-t <id>`; the positional slot is reserved for the path. | Avoids positional-vs-path ambiguity; keeps the two-stage parse simple; matches modern System.CommandLine conventions. |
| `func templates list` runs in any directory (v4 `ListTemplatesAction`) | Requires an init'd Functions project — uninitialised directory exits 1 with a hint pointing at `func init` (§5.5.5 / §8 Q11). No `--stack` override. | v5's channel-resolution algorithm (§4.8.1) keys off `host.json` `extensionBundle.id`; a stackless catalogue has no defined channel selection. Users browsing stacks before initialising should use `func quickstart list` (separate command, no channels). |
| `func function new` / `func function create` aliases | Dropped (see §5.1). Single canonical verb is `func new`. | One verb, one source of truth; legacy aliases produce a standard unknown-command hint pointing at `func new`. |
| Installs missing NuGet extensions via `Metadata.Extensions` | Dropped — relies on extension bundles | Bundle resolver covers this in vnext. |
| `_templatesManager.Deploy(...)` writes files directly | `provider.ApplyAsync(NewContext, ParseResult)` returns a result | Errors are typed (sealed hierarchy); writes happen behind the provider. |
| Two prompt loops (`RunUserInputActions` for v2, ad-hoc for v1) | Single hydration: every prompt becomes a real `Option<T>` via the engine-agnostic `TemplateOptionHydrator.Hydrate(template)` (§4.5). The parser is the interaction surface. v5 templates workloads ship v2 only — the ad-hoc V1 loop is dropped along with the V1 engine. | One uniform CLI surface; same hydration for v2 (Node, Python) and DotNet. |
| `IsNewPythonProgrammingModel()` / `IsNewNodeJsProgrammingModel()` heuristics select v2 vs v1 engine; `-4.x` template-id suffix routes Node v4 to a separate code path | Dropped. v5 templates workloads ship v2 templates only (templates-workload-spec.md §5.2); the legacy V1 `Files`-map engine is removed from vnext entirely. Engine is derived from the template payload's directory (`content/v2/` → V2, `content/dotnet-templates.json` at content root → DotNet; §4.3 / D9 Q-Eng-A). Workload publishers ship only current-generation templates (D9 Q-Eng-B). | One uniform routing rule; no runtime sniffs to maintain. |
| `--programming-model` knob + post-deploy "Python v1 → v2" / "Node v3 → v4" upgrade messaging | Both dropped. No model flag, no migration nudge — workloads curate to "latest" and publish only current-generation templates (D9 Q-Eng-B). | Cleaner UX; legacy-model upgrade is a workload-version concern, not a per-`func new` axis. |
| Language inferred from the first `--language`-like flag on `func new`; language asked anew on every invocation in some paths | Flag dropped. Language is **read** via DI from `IOptionsMonitor<StackOptions>.Get(workingDirectory.FullName).Language` — the bound view of `.func/config.json` `stack.language` (`StackOptionsSetup` is the single file-read path; `func new` consumes the snapshot). `func init` writes the value from **its** `--language` flag (PR #5125 omits the key for single-language stacks; `func new` substitutes the stack's canonical language in that case). `func new` requires an init'd project; a missing key on a multi-language stack → exit 1 pointing at `func init`. | One deterministic source of truth via the existing options pipeline; eliminates a redundant flag, the alias-resolution code path, the prompt loop, **and** any per-invocation file I/O. Matches the precedent in `ProfileResolver` / `SetupRunner` for `IOptionsMonitor<ProjectProfileOptions>`. |
| No concept of templates-channel; bundle and templates were the same package | Templates ship as their own per-channel content workload; `func new` auto-matches the project's bundle channel (§4.8.1) with no cross-channel fallback. Missing match → `NoTemplatesWorkloadForChannel` with install hint. | Per templates-workload-spec.md §4.4.2; lets templates revisions ship independently of bundle releases. |
| No `minBundleVersion` check at scaffold time | `func new` enforces the templates workload's `minBundleVersion` against the project's resolved bundle version (§4.8.2); violation → `MinBundleVersionTooOld` and exit 1 (no warning fallback in v1). | Per templates-workload-spec.md §4.4.4; catches binding compatibility issues before any file is written. |
| `--profile <name>` and `--offline` mirrored from `func run` on every command | Dropped from `func new` and `func templates list`. Active profile is resolved silently from `.func/config.json defaultProfile → user prefs → built-in default`; stack-vs-profile gate still fires (pipeline step 3). Mismatch error hints at `func profile set <other>` / `func profile list`. | Scaffolds are static files — there is no scaffold-under-profile-X-then-run-under-Y use case. Profile is a project-level pin, not a per-invocation override; `--offline` only refreshed profile sources that scaffold output doesn't depend on. |

Telemetry continuity: emit `cli.new.invoked` (stack, language, template id,
engine id) plus `cli.new.duration` histogram and `cli.new.outcome`
(created / already-exists / failed). Failure subtypes go on
`cli.new.failure_kind`. The `engine id` axis lets us track v2 vs dotnet
adoption without surfacing engine identity to users (D9 / Q-Eng-A).

## 8. Open questions

Promote to §4 once answered.

- ✅ **Q1. `func new --list` vs `func templates list`. — RESOLVED 2026-05-27.**
  Discovery lives only on `func templates list` (catalog-only — NAME / TRIGGER
  / DESCRIPTION, §5.5). `func new --list` is **not** added. No `--detailed`
  flag on `func templates list` either: per-template options surface on
  `func new --template <id> --help` via two-stage hydration (§4.5). No
  `func templates show <id>` either — `func new --template <id> --help` is
  the per-template detail surface. Single source of truth for "what does this
  template need."
- ✅ **Q2. Are template-declared NuGet extension installs needed at all? — RESOLVED 2026-05-28.**
  **Dropped entirely.** The legacy `Template.Metadata.Extensions` path
  (which appended `<PackageReference>` entries to a project-local
  `extensions.csproj`) serves zero v5 templates: Node and Python ship
  v2-only templates whose bindings are delivered by the extension
  **bundles workload** (`Workloads.ExtensionBundles`,
  bundles-workload-spec.md); PowerShell stays on extension bundles in
  v5; DotNet uses worker-SDK `<PackageReference>` entries that the
  `dotnet new` template adds directly to the user's `.csproj`. There is
  no `extensions.csproj` build step in a v5 project either — `func start`
  resolves bindings entirely through the bundles workload at launch
  time, not via a per-project NuGet restore. `func new` therefore does
  **not** consult `Template.Metadata.Extensions` on vnext; if a workload
  payload happens to carry the field, it is ignored. Workload publishers
  should not author it in v5 templates.
- ✅ **Q3. `Templates.V2` + `Templates.DotNet` — one assembly or two? — RESOLVED 2026-05-28.**
  **Two separate CLI-internal csprojs**, project-referenced from
  `Func.csproj`, named `Templates.V2` and `Templates.DotNet` per the
  standard naming rule (avoiding the `Func.` prefix that's reserved for
  the entry-point binary and its tests per AGENTS.md). Each project
  hosts exactly one `ITemplateEngineProvider` plus its engine
  implementation; each has its own paired test project
  (`Templates.V2.Tests` / `Templates.DotNet.Tests`). Per-engine
  isolation keeps each engine's quirks (V2's job/action DSL + `$(KEY)`
  substitution, DotNet's `dotnet new` shellout + JSON catalog reader)
  out of `Func.csproj`'s dependency graph and makes the engine boundary
  explicit at the project-reference level. The legacy V1 engine project
  (`Templates.V1`) is **not** part of vnext — v5 templates workloads
  ship v2 only (templates-workload-spec.md §5.2).
- ✅ **Q4. `IStackLanguageDetector` registration — bare or tagged? — RESOLVED 2026-05-28.**
  Not applicable. `IStackLanguageDetector` is dropped from the vnext design
  (D10 revised). Language resolution reads `.func/config.json`
  `stack.language` directly via `StackOptions` (PR #5125's omit-on-single-
  language behaviour drives the fallback); no per-stack detector seam is
  needed. The remaining seams (`ITemplateEngineProvider`,
  `IProjectInitializer`) are unaffected.
- ✅ **Q5. `--prop key=value` vs hydrated `Option<T>` per prompt. — RESOLVED 2026-05-27.**
  Every prompt hydrates as a real `Option<T>` via the engine-agnostic
  `TemplateOptionHydrator.Hydrate(template)` (§4.5). No `-p`/`--prop`
  catch-all. `--help` is self-documenting, autocompletion works, validation
  runs through the parser. Pollution concern dissolved because hydrated
  options only appear under `func new --template <id> --help` — top-level
  `func new --help` stays template-agnostic (built-ins only).
- ✅ **Q5b. Can `func new --help` (top-level) be hydrated? — RESOLVED 2026-05-27.**
  **Partially, yes.** Top-level `func new --help` renders an **Available
  templates** catalog section (§5.2.1) showing short name + display name +
  description for each template the active stack's provider exposes. Tab
  completion on `--template <TAB>` uses the same catalog. **Per-template
  option flags do not appear at the top level** — option-name collisions
  across templates (every template has `--namespace`), help-text explosion,
  and the stage-A parser's inability to pick a hydration target before
  `--template` is bound make that infeasible. Per-template options stay
  under `func new --template <id> --help` (Q5 stands).
- ✅ **Q6. CSX (`.csx` DotNet functions) — do we keep `--csx` in vnext? — RESOLVED 2026-05-28.**
  **Dropped.** `.csx` script-mode functions are not scaffoldable on v5
  and `--csx` is not carried forward. Three reinforcing reasons:
  (a) the v5 `Workloads.Templates.DotNet` content workload sources its
  catalog from `Microsoft.Azure.Functions.Worker.ItemTemplates` — the
  **isolated-worker** item-templates package, which ships compiled
  (`.cs`) item-templates only; no `.csx` script templates exist
  upstream to hydrate into `dotnet-templates.json` (§5.3 /
  templates-workload-spec.md §5.3.1, §6.3).
  (b) `.csx` is fundamentally an in-process .NET runtime concept;
  vnext-design Part IX item 9 establishes that the v5 CLI does **not**
  support in-process .NET ("this is a CLI-level constraint enforced
  before host launch, independent of profiles. The CLI detects
  in-process projects from project files and exits with a clear
  error").
  (c) The legacy V1 engine that hosted CSX scaffolding is removed from
  vnext (Q-Eng-C); reintroducing CSX would require both an in-process
  runtime workload and a v5-shaped templates content workload that
  ships `.csx` items — neither exists.
  Users on `.csx` stay on v4 tooling. Migration row in §7 captures
  this. If in-process .NET is ever reconsidered on v5, CSX rides along
  as a new `Workloads.Templates.DotNet.Csx` content workload (or
  similar) — not a built-in `--csx` CLI flag.
- ✅ **Q7. Should `func new` also accept `func new <template>` as a positional shortcut? — RESOLVED 2026-05-28.**
  **No — explicit `--template` / `-t` only.** Three reasons:
  (a) `func new` already takes one positional argument — the project
  path, inherited via `AddPathArgument()` (§5.2). A second positional
  for the template id collides for the common single-arg case
  (`func new HttpTrigger` is indistinguishable from
  `func new <some-path>` without out-of-band knowledge); System.CommandLine
  has no clean disambiguation.
  (b) The two-stage parse (§4.5) needs `--template` to be a stable,
  discoverable option in stage A. A positional shortcut adds a parallel
  resolution path and makes stage-A failure modes harder to reason about.
  (c) Modern .NET CLI convention favours explicit options; `dotnet new`
  takes the template short-name positionally only because it has no
  path-positional collision. v5 `func new` does.
  Users who type the v4 form get a standard System.CommandLine parse
  error pointing them at `--help`. The §7 Migration table captures the
  break.
- ✅ **Q8. Where does `ITemplateEngineProvider` live — new `Templates` namespace in Abstractions or reuse `Projects`? — RESOLVED 2026-05-28.**
  **New `Azure.Functions.Cli.Templates` namespace** under
  `src/Abstractions/Templates/`. Holds `ITemplateEngineProvider`,
  `FunctionTemplateInfo`, `TemplateListContext`, `NewContext`,
  `TemplateApplicationResult`, and `TemplateApplicationFailure`. Three
  reasons:
  (a) Matches the per-domain Abstractions convention (`Projects/`,
  `Bundles/`, `Workers/`, `Quickstart/` each own one cohesive contract
  surface).
  (b) `Quickstart/` is the direct precedent — it could have lived under
  `Projects/` (it also scaffolds) but got its own folder because it is
  a distinct user-facing verb (`func quickstart`) with its own
  contracts. `func new` mirrors that: distinct verb, distinct contracts,
  distinct lifecycle (post-init, picks one template, hydrates options).
  (c) Aligns with the CLI-internal engine project naming from Q3
  (`Templates.V2`, `Templates.DotNet`).
  `Projects/` continues to own the scaffold-the-whole-app contracts
  (`IProjectInitializer`, `IFunctionsProjectFactory`, `InitContext`); the
  two contract surfaces stay cleanly separated.
- ✅ **Q9. `--name` validation rules. — RESOLVED 2026-05-28.**
  **Option C: template metadata is authoritative, with a per-stack
  default fallback.** Two layers, in this order:
  1. **Template-metadata validator (authoritative).** If the template
     declares a validator for its function-name prompt — V2
     `UserPrompt.validator` (regex), DotNet
     `dotnet-templates.json` `parameters[].constraints` (projected from
     the upstream `template.json` at workload pack time,
     templates-workload-spec.md §5.3.1) — `TemplateOptionHydrator`
     (§4.5) projects it as the `Option<string>`'s validator / parser
     callback. This is the source of truth; the CLI never overrides it.
  2. **Per-stack default (fallback).** When the template's function-name
     prompt declares no validator, `NewCommand` consults a stack-default
     validator surfaced through the active stack's `IProjectInitializer`.
     A new optional member on the existing init interface
     (`Regex? DefaultFunctionNameValidator { get; } => null`, or
     `bool TryValidateFunctionName(string name, string language,
     out string? error)` — exact shape settled at implementation time;
     non-breaking default for stacks that don't supply one) keeps the
     regex alongside the stack code that already knows the language's
     identifier and filesystem rules. No new top-level contribution
     seam, no per-template duplication.
  **Why both layers.** Template-first matches the D5 / Q5 "every prompt
  is a real `Option<T>` driven by metadata" stance and lets a template
  with unusual substitution rules (e.g. a DotNet template that allows
  dotted namespaces in the function name) override without a CLI change.
  Stack-default ensures untrusted-template-author and missing-validator
  cases still produce a sensible error before any file is written.
  **Examples.**
  - DotNet HttpTrigger ships
    `symbols.namespace.constraints` etc. on the upstream `template.json`
    → projected into `parameters[]` → hydrator applies the regex. CLI
    fallback unused.
  - Node v2 `HttpTrigger-JavaScript` declares its function-name input
    with `validator: "[A-Za-z_][A-Za-z0-9_]*"` (or no validator) →
    template-first if present, Node-default fallback otherwise (JS
    identifier + filesystem-safe).
  - Python v2 same shape; default falls through to Python identifier
    rules.
  **Error UX.** Validation failure surfaces as a standard
  `System.CommandLine` parse error (Stage B), printed via
  `IInteractionService`, exit 1 — same channel as any other typed
  option failure.
- ✅ **Q10. How does `func new` learn the programming model? — RESOLVED 2026-05-28 (D9 / Q-Eng-B).**
  Not relevant. vnext drops the programming-model concept entirely.
  Workload publishers curate to ship only current-generation templates
  (e.g. the Node templates content workload ships v4 templates only; the
  Python templates content workload ships v2 templates only). No runtime
  sniff (`IsNewPythonProgrammingModel`, `IsNewNodeJsProgrammingModel`,
  `-4.x` suffix), no `--programming-model` flag, no
  `TemplateListContext.ProgrammingModelHint`. If a workload publisher
  needs to support both generations in the same release window, they can
  ship two separate templates content workloads (different package ids,
  different channels).
- ✅ **Q11. `func templates list --output json` envelope shape. — RESOLVED 2026-05-28.**
  Single envelope `{ stack, language, templates: [ … ] }` per §5.5.4 —
  every invocation. There is no multi-stack case because §5.5.5 now
  requires an init'd project (no stackless run, no `--stack` override):
  every invocation resolves to exactly one stack + language. Tooling
  parses one shape always; no NDJSON, no `{ stacks: [...] }` wrapping
  array. The shape is identical for plain and JSON output paths — JSON
  just promotes the rendered fields into structured form (§5.5.4) and
  adds DotNet-only `identity` / `groupIdentity` / `classifications` /
  `shortNames[]` when applicable (templates-workload-spec.md §5.3.1).
- ✅ **Q12a. Failure UX when no installed templates workload matches the
  project's bundle channel (Node/Python).** — RESOLVED 2026-05-27 (§4.8.1,
  §5.5.3, §6 step 4b). Fail with
  `NoTemplatesWorkloadForChannel(stack, channel, suggestedPackageId, suggestedVersion)`,
  exit 1. Hint text identifies the specific channel-suffixed pkg id +
  version the user needs to install. No cross-channel fallback (a stable
  templates pkg does **not** satisfy an experimental project, per
  templates-workload-spec.md §4.4.2).
- ✅ **Q12b. Failure UX when the selected templates workload's
  `minBundleVersion` is newer than the project's resolved bundle
  (Node/Python).** — RESOLVED 2026-05-27 (§4.8.2, §5.5.3, §6 step 11b).
  Fail with
  `MinBundleVersionTooOld(installedBundleVersion, requiredRange, templatesWorkloadVersion)`,
  exit 1. v1 picks **error** over warning so partial-write windows stay
  empty; if real users hit false positives, a future
  `--ignore-min-bundle` opt-out can soften to a warning. Decision is
  stricter than templates-workload-spec.md §4.4.4's "warning or error"
  hedge — this spec owns the enforcement timing & severity.
- ✅ **Q12c. Should `func setup` install templates content workloads? — RESOLVED 2026-05-28 (out of scope).**
  **Out of scope for this spec.** `func setup`'s install set is owned
  by `docs/proposed/func-setup-design.md` (refreshed in PR #5098), which
  currently excludes templates content workloads (working-context
  §3.18). `func new` continues to surface a per-channel install hint
  via the Q12a `NoTemplatesWorkloadForChannel` failure when no matching
  templates workload is installed — that's the supported acquisition
  path. Whether `func setup` should later grow templates support
  (and how it would pick a channel without a project's `host.json`
  signal — `func setup` runs before any project exists) is a decision
  for the `func-setup-design.md` owners, not for this spec.
- ✅ **Q-Lang-1. Persisting language in `.func/config.json`. — RESOLVED 2026-05-29 (D10 revised).**
  **Yes, persisted — and consumed via DI options binding.** `func init`
  writes `stack.language` to `.func/config.json` when its `--language`
  flag is supplied (`src/Func/Commands/InitCommand.cs:266`,
  `CliConfigurationNames.StackLanguageKey = "language"`). PR #5125
  intentionally **omits** the key when the active stack is single-language
  (Python, PowerShell, Java, Custom, Go); for those cases the CLI
  substitutes the stack's canonical single language at read time.
  `func new` reads back the configured value by injecting
  `IOptionsMonitor<StackOptions>` from DI and calling
  `.Get(workingDirectory.FullName).Language` — the standard project-scoped
  options pattern used elsewhere in the codebase
  (`Profiles/ProfileResolver.cs`, `Commands/Setup/SetupRunner.cs`,
  `Commands/Profile/ProfileListCommand.cs` all inject
  `IOptionsMonitor<ProjectProfileOptions>` the same way).
  `StackOptionsSetup` (registered as
  `IConfigureNamedOptions<StackOptions>` in
  `Hosting/CliHostFactory.cs:109`) is the single file-read path; `func new`
  never opens `.func/config.json` itself, satisfying AGENTS.md's
  "Bind configuration via `IOptions<T>` / `IOptionsMonitor<T>`; do not
  read `IConfiguration` from business logic" rule. No fingerprinting, no
  per-stack detector seam, no schema change beyond what's already
  shipping on `vnext`. Supersedes the earlier D10 stance ("init artifacts
  ARE the persistence layer; no config field"): the code already had a
  config field bound through DI; the spec now matches reality.
- ✅ **Q-Lang-2. Mixed-language repos (`.csproj` and `.fsproj` both present). — RESOLVED 2026-05-29 (D10 revised).**
  Not a concern at `func new` time. Mixed-language detection lives in the
  project-resolver layer (e.g. `NodeProjectFactory` / `DotNetProjectFactory`);
  `func new` reads `stack.language` from `.func/config.json`, which
  `func init` pinned to a single canonical value. If a user manually edits
  the project to add a competing language file post-`func init` without
  re-running `func init`, the value in `.func/config.json` is still
  authoritative — `func new` scaffolds for whatever language `func init`
  recorded. If `.func/config.json` is missing the key on a multi-language
  stack, `func new` exits 1 pointing at `func init`.
- ✅ **Q-Lang-3 / D10. `--language` flag on `func new` and `func templates list`. — RESOLVED 2026-05-29 (revised).**
  Dropped from both commands. Language is **read via DI** from
  `IOptionsMonitor<StackOptions>.Get(workingDirectory.FullName).Language`
  — the bound view of `.func/config.json` `stack.language` that
  `func init` wrote from **its** `--language` flag. `func new` cannot run
  before `func init`, so the source of truth is always present. The
  command does not open `.func/config.json` itself — `StackOptionsSetup`
  is the single file-read path, and the options pipeline caches/refreshes
  the snapshot, so per-invocation file I/O at scaffold time is avoided. A
  `func new --language` flag would either contradict the init choice
  (drift) or be a no-op when it agrees. `func templates list` requires an
  init'd project for the same channel-resolution reasons (§5.5.5 / Q11),
  so it has no `--language` flag either.
- ✅ **Q-Profile-1 / D11. `--profile` and `--offline` flags on `func new` and `func templates list`. — RESOLVED 2026-05-27.**
  Dropped from both commands. Profile selection is a project-level pin set
  by `func profile set` (or `.func/config.json defaultProfile`, user prefs,
  or the built-in default — resolved silently by `IProfileResolver`).
  Scaffolds and template discovery are static, deterministic operations;
  there is no scaffold-under-profile-X-then-run-under-Y use case worth a
  per-invocation override. The stack-vs-profile gate (§4.6, pipeline step 3)
  still fires against the resolved active profile; mismatch errors hint at
  `func profile set <other>` and `func profile list`. `--offline` (refresh
  remote profile sources) is irrelevant at scaffold time — the resolved
  profile is already in hand and no scaffold output depends on a fresh
  remote refresh. Mirrors the D10 pattern: drop redundant flags when the
  deeper config already encodes the answer. `func run` keeps both flags,
  since execution under a non-pinned profile is a legitimate use case there.
- ✅ **Q-Eng-A. How does the CLI know which engine to use for a given template? — RESOLVED 2026-05-28 (D9, revised 2026-05-29).**
  Implicit by the template payload's **directory layout** inside its
  workload (templates-workload-spec.md §5.2, §5.3):
  presence of `content/v2/templates/templates.json` → V2 engine (Node and
  Python); presence of `content/dotnet-templates.json` at the content root
  → DotNet engine. A given templates workload ships exactly one engine
  zone, so there is no tiebreak — Node/Python ship v2 only (the legacy V1
  engine is removed from vnext), DotNet ships `dotnet-templates.json`
  only. No engine field on the template, no `--engine` flag, no help text.
  Engine identity is CLI-internal dispatch (§4.3); end users only see
  templates by stack + name.
- ✅ **Q-Eng-B. Programming-model axis — runtime knob or publisher concern? — RESOLVED 2026-05-28 (D9).**
  Publisher concern. vnext has no `programmingModel` field on templates,
  no `FunctionTemplateInfo.ProgrammingModel`, no
  `TemplateListContext.ProgrammingModelHint`, no `--programming-model`
  flag, no `func templates list` grouping by model, no post-deploy
  upgrade messaging. Workload publishers ship only current-generation
  templates ("latest" is what the workload version ships, not a runtime
  axis). Main-branch sniffs (`IsNewPythonProgrammingModel()`,
  `IsNewNodeJsProgrammingModel()`, `-4.x` suffix routing) are dropped.
- ✅ **Q-Eng-C. V1 engine layout — per-template, mixed, or workload-level? — RESOLVED 2026-05-29 (V1 engine removed).**
  Not applicable. v5 templates workloads ship v2 only
  (templates-workload-spec.md §5.2: "v1 (legacy) programming-model
  templates are not shipped in v5 templates workloads"). The CLI does
  **not** include a V1 engine project. The legacy `layout`
  (`function-folder` / `src-functions`) workload manifest field is moot —
  v2 encodes write paths entirely in `TemplateAction.FilePath`.
- ✅ **Q-Eng-D. DotNet engine — separate plugin contract or hybrid catalog + shellout? — RESOLVED 2026-05-28 (D9).**
  Hybrid. The DotNet templates content workload ships
  `content/dotnet-templates.json` (fully-hydrated catalog + per-template
  `parameters[]` index, templates-workload-spec.md §5.3.1) and a sibling
  `content/source.json` (NuGet pin for the upstream item-templates
  package, templates-workload-spec.md §5.3, §6.3). At
  `func workload install` time the CLI provisions the pinned NuGet pkg
  into a CLI-managed dotnet template hive. The CLI's `DotNetEngineProvider`
  reads the JSON for `func templates list` and `func new --template <id>
  --help` (offline-deterministic, no `dotnet new` invocation, no NuGet
  I/O) and shells out via `IDotnetCliRunner` (`dotnet new <shortName>
  --<param> <value> …`) only for the actual apply step. Workloads stay
  content-only — no DLL plugin contract. This partially supersedes D7's
  "no separate DotNet engine project" stance: the catalog reader +
  shellout coordinator now live in their own `Templates.DotNet` project to
  match V2.

## 9. References

- `working_context_templates_workload_design.md`
  - §3B legacy `func new` surface (main)
  - §3.5 `IProjectInitializer` + `IInitOptionRegistry` (the precedent)
  - §3.9 `IExtensionBundleResolver`
  - §3.10 project model
  - §3.13 filesystem conventions
  - §3.14 result/failure record pattern
  - §3.17 profile system
  - §3.18 `func setup`
  - §3B.4 legacy decision tree
  - §3B.6 the three template engines
  - §3B.7 mapping to vnext
- `templates-workload-spec.md` — templates workload spec (payload, layout,
  packaging; §5.2 Node/Python v2-only payload, §5.3 / §5.3.1 DotNet
  `dotnet-templates.json` schema, §6.3 hive provisioning at install time)
- `src/Func/Commands/NewCommand.cs` — current placeholder
- `src/Func/Commands/InitCommand.cs` — the `func init` orchestrator (writes
  `stack.language` to `.func/config.json` per §4.7 / D10 revised; template
  to mirror for `func new`)
- `src/Func/Configuration/StackOptions.cs` —
  `Runtime` / `Language` DTO that backs `stack.runtime` / `stack.language`
- `src/Func/Configuration/StackOptionsSetup.cs` —
  `IConfigureNamedOptions<StackOptions>`; the **single code path** that
  reads `.func/config.json` for the `stack` section and binds it into
  `StackOptions`. Consumed via `IOptionsMonitor<StackOptions>.Get(projectDir)`.
- `src/Func/Hosting/CliHostFactory.cs:109` — registers
  `AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>()`.
- `src/Func/Configuration/CliConfigurationNames.cs` —
  `StackSectionName = "stack"` / `StackLanguageKey = "language"`
- `src/Func/Profiles/ProfileResolver.cs` — precedent for project-scoped
  options consumption: injects `IOptionsMonitor<ProjectProfileOptions>`,
  reads via `.Get(projectDirectory)`. `func new` follows the same pattern
  for `IOptionsMonitor<StackOptions>` (§4.7).
- `src/Func/Commands/Setup/SetupRunner.cs` — same precedent
  (`IOptionsMonitor<ProjectProfileOptions>`).
- `src/Abstractions/Projects/IProjectInitializer.cs` — direct precedent for
  the bare engine-provider contract (`ITemplateEngineProvider` is
  CLI-internal)
- `src/Abstractions/Projects/IInitOptionRegistry.cs` — option-dedup machinery
- `src/Workloads/Stacks/DotNet/DotNetProjectInitializer.cs` — `dotnet new func`
  + template hive precedent (closest analogue for the DotNet template provider)
- `src/Abstractions/Commands/InitContext.cs` — direct precedent for
  `NewContext`
- Main-branch `src/Cli/func/Actions/LocalActions/CreateFunctionAction.cs` —
  the 624-line legacy implementation `func new` replaces
