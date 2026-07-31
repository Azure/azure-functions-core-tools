# Context — adopt-ms-template-engine

> **Audience:** AI agents (and humans) building context to contribute to this
> change in parallel. Read this file and you should not need to open the
> source specs or the templating-engine repo to understand the landscape.
> The **decisions and target architecture** live in [`design.md`](./design.md)
> — this file is background only.
>
> **⚠️ State as of 2026-07-30 (read first):** the design pivoted (design
> D21–D31): **templates are no longer workloads.** Template packages are
> standard engine packages identified by NuGet package types
> `FuncItemTemplates` / `FuncAppTemplates`, acquired via the engine's
> `TemplatePackageManager` into an isolated func hive (engine cache =
> installed-state truth), with **template search** (discovery-service-built
> index) and **project templates in `func init`** (per-stack `Empty`,
> auto-install of official packages, thin `IProjectInitializer` metadata
> contract). Sections below marked *HISTORICAL* describe the pre-pivot
> workload-based model — kept for background on what the implementation
> currently on the branch does, and for why decisions changed. §10 is the
> authoritative summary of the pivot architecture.
>
> **Contribution rules:**
> - New *decisions* → `design.md` §7 decision log (never here).
> - New *facts learned* about existing code / the MS engine → here.
> - Session checkpoints → §12 below.

---

## 1. Resource inventory

| Resource | Location | Notes |
|---|---|---|
| Current repo / branch | `C:\root\worktreeRepos\core-tools\TemplatesV3` (branch `nasoni/templatesV3`) | v5 CLI; func-new spec largely implemented |
| Workload spec | `core-tools\docs\proposed\workload-spec.md` | Summarized §2 |
| Templates workload spec | `core-tools\docs\proposed\templates-workload-spec.md` | Summarized §3 |
| `func new` spec | `core-tools\docs\proposed\func-new.spec.md` | Summarized §4 |
| MS templating engine code | `C:\root\worktreeRepos\templatingEngine\code` | `Microsoft.TemplateEngine.*` sources |
| MS templating engine docs | `C:\root\worktreeRepos\templatingEngine\documentation` | Hosting guide, template.json reference, post-action registry |
| **Functions templates source** | `C:\root\repos\templates\Functions.Templates\Templates` | **We own this repo.** 237 template dirs, all languages. C#/F# variants are real `template.json` (feed the `Worker.ItemTemplates` nupkgs); JS/TS/Python/PowerShell are v4-format (`function.json` + `metadata.json`). Key facts: **no `dotnetcli.host.json` anywhere** (only `vs-2017.3.host.json` VS host files); isolated templates use the add-reference post action `B17581D1…` (TFM-conditional extension `PackageReference`, empty manual instructions) and `msbuild:TargetFramework` bind symbols; open-in-editor post action is conditioned on `HostIdentifier`. The repo **also publishes `Microsoft.Azure.Functions.Worker.ProjectTemplates`** (verified in `Build/PackageFiles/Dotnet_precompiled/*.nuspec`; ItemTemplates declares packageType `Template`) — a real .NET *project*-template corpus already in engine format (basis of design D28) |
| **TemplateDiscovery tool** | `C:\root\worktreeRepos\templatingEngine\code\Tools\Microsoft.TemplateSearch.TemplateDiscovery` | The scraper behind the `dotnet new search` index; basis of the func discovery service (design D22). See §11 for how it works |
| **TemplateSearch.Common** | `C:\root\worktreeRepos\templatingEngine\code\Microsoft.TemplateSearch.Common` | CLI-side search consumer (`NuGetMetadataSearchProvider`, `TemplateSearchCoordinator`, search-cache model). See §11 |

---

## 2. The v5 workload model (from workload-spec.md)

> Still-relevant background: workloads remain the delivery vehicle for
> **stacks, workers, the host runtime, and extension bundles** — and the
> stack workload is still required at `func init` (D31). But **templates
> are no longer workloads** (D21, 2026-07-30): nothing below applies to
> template packages anymore.

The v5 `func` CLI is a small, stable binary extended by **workloads** — NuGet
packages installed under `~/.azure-functions` (the *workload home*,
configurable via `FUNC_CLI_Workloads__Home`) and recorded in
`<workload-home>/workloads.json` (schema-versioned via `$schema`; unknown
schema → `GracefulException`).

### 2.1 Workload kinds

| Kind | Carries | Loaded? |
|---|---|---|
| `workload` | Entry-point assembly deriving from abstract `Workload` (`Func.Cli.Abstractions`) | Yes — collectible `AssemblyLoadContext` per workload; `Workload.Configure(FunctionsCliBuilder)` registers DI services |
| `content` | Files only under `tools/any/` | No — registry row only; consumers resolve `<workload-home>/workloads/<packageId>/<version>/` by convention |
| `meta` | Nothing; bundles others via nuspec `<dependencies>` | No |

Mechanics that matter for this change:

- **Package layout:** `workload.json` at package root (`$schema`, `kind`,
  `entryPoint` for `kind: workload`); payload under `tools/any/`. Packages are
  **self-contained** (no transitive NuGet restore at install).
- **Activation:** registry read once per CLI start; **highest installed semver
  per package id is live**; `content`/`meta` never loaded. Startup budget
  <100 ms for 25 workloads; content rows cost only the registry read.
- **Contribution points (DI):** `IProjectInitializer`, `IProjectDetector`,
  `ITemplateProvider` (concept), `IPackProvider`, `IExternalCommand`.
- **Errors:** `GracefulException` = user-facing verbatim; anything else =
  protocol error prefixed `[<workload-id>]`.
- **Offline:** commands like `func new` perform no network I/O; only
  install/update/search hit the catalog.
- **Nuspec conventions:** `FuncCliWorkload` packageType required;
  `alias:<name>` tags for short install names; `minBundle:<range>` tag
  (templates) for discoverability.
- Install/update/uninstall/prune are idempotent, atomic (staging + rename),
  cross-process locked; side-by-side versions allowed (`--force`), "highest
  wins" at load; `prune` removes non-live versions.

### 2.2 Precedent packages

`Azure.Functions.Cli.Workloads.Host.<rid>` (host runtime, content),
`...Workers.<stack>`, `...Stacks.<stack>`, `...ExtensionBundles`, and the
three templates workloads (§3). The CLI's responsibility for content packages
ends at install/registry; the consumer owns the payload contract.

---

## 3. HISTORICAL — the templates workload design (templates-workload-spec.md)

> This entire section describes the **pre-change and pre-pivot** model
> (content workloads, channels, sidecar manifest, hive provisioning). All
> of it is retired by D16 (constraints) and D21 (engine acquisition).
> Kept because the implementation currently on the branch still works this
> way, and migration tasks need to know what's being deleted.

Three `kind: content` packages, one per stack:

```
Azure.Functions.Cli.Workloads.Templates.Node
Azure.Functions.Cli.Workloads.Templates.Python
Azure.Functions.Cli.Workloads.Templates.DotNet
```

### 3.1 Node / Python payload (V2 format — replaced by this change)

```
tools/any/content/
  templates-workload.json      ← sibling manifest: { "minBundleVersion": "[4.0.0, )" }
  v2/
    bindings/userPrompts.json  ← UserPrompt[] (validators/enums/labels, referenced by paramId)
    resources/Resources*.json  ← i18n strings ($key syntax)
    templates/templates.json   ← NewTemplate[] jobs/actions DSL + inline files map
```

- Template **files are inline strings** in each entry's `files` map; `$(VAR)`
  tokens substituted at scaffold time. No on-disk file tree.
- **Node:** static, hand-curated in-repo
  (`src/Workloads/Templates/Node/content/v2/`; 33 templates, JS+TS variants)
  because the upstream extension bundle publishes no v2 Node entries.
  Per-channel subsetting at pack time: committed `_bindings.json`
  (template id → required binding names) cross-referenced against the
  channel's bundle `bin/extensions.json`, fetched via HTTP-Range from the CDN
  zip (`eng/scripts/fetch-bundle-extensions-json.ps1`,
  `filter-node-templates-by-bundle.ps1`). Snapshot: stable 31/33,
  preview 33/33, experimental 31/33 (the `McpPromptTrigger` pair drops where
  the `mcpPromptTrigger` binding hasn't propagated).
- **Python:** filtered snapshot of the bundle's `StaticContent/v2` tree at
  workload build time (`language == "python"`).
- **Channel axis** (stable/preview/experimental) encoded as the pkg version's
  prerelease label; **auto-derived at `func new` time** from `host.json`
  `extensionBundle.id` (`Microsoft.Azure.Functions.ExtensionBundle[.Preview|
  .Experimental]` → no label / `-preview` / `-experimental`). No flag, no
  cross-channel fallback.
- **minBundleVersion** (NuGet range, e.g. `[4.18.0, )`) in the sibling
  manifest (authoritative) + `minBundle:` nuspec tag + description sentence;
  enforced as a **hard error** at `func new` time.

### 3.2 DotNet payload (catalog + pin — replaced by this change)

```
tools/any/content/
  dotnet-templates.json   ← catalog + parameters[] per template, projected at
                            workload BUILD time from the pinned NuGet pkg's
                            template.json (+ dotnetcli.host.json) files
  source.json             ← { kind: "nuget", packageId:
                            "Microsoft.Azure.Functions.Worker.ItemTemplates",
                            version: "<pinned>" }
```

- At `func workload install` time the CLI provisions the pinned pkg into a
  CLI-managed dotnet template hive (`dotnet new install <id>::<ver>
  --debug:custom-hive <hive>`, sentinel `.installed.<pkg>.<ver>`).
- Read paths served offline from `dotnet-templates.json`; scaffold shells out
  `dotnet new <shortName> --<param> <value>` against the hive. Requires
  `dotnet` on PATH; 5-minute provisioning timeout.
- The `dotnet-templates.json` schema (id/shortNames/identity/groupIdentity/
  name/description/author/language/type/classifications/defaultName/
  constraints/parameters[] with choices, isRequired, isHidden,
  shortNameOverride/longNameOverride from `dotnetcli.host.json`) is a faithful
  down-projection of `template.json` — evidence the mapping in §8 is complete.
- No channel axis, no minBundleVersion.

### 3.3 Principles from that spec preserved by this change

- Templates ship **only** via installed workloads; CLI has zero built-ins.
- `func new` fully **offline at invocation**; payload acquired at workload
  build/install time.
- No auto-install; missing/unsatisfying workloads → actionable
  `func workload install` hint, exit non-zero.
- Per-stack packages; language is per-template metadata within a stack.
- Template metadata drives option hydration — new template inputs require
  **no CLI release**.
- Channel + minBundleVersion selection policy — orthogonal to the engine and
  carried over unchanged.

---

## 4. Current `func new` design (func-new.spec.md) + implementation state

### 4.1 Pipeline (implemented in `src/Func/Templates/NewCommandRunner.cs`)

```
1. ResolveProfile          (IProfileResolver; stack ∈ profile.SupportedRuntimes)
2. ResolveProject          (IFunctionsProjectResolver — hard gate; no auto-init)
3. ValidateStack           (profile check)
4. SelectTemplatesWorkload (Node/Python: host.json bundle id → channel →
                            highest installed matching pkg; DotNet: highest)
5. ResolveLanguage         (IOptionsMonitor<StackOptions>.Get(dir).Language;
                            single-language stacks substitute canonical id;
                            multi-language + missing → error → func init)
6. ListTemplates           (aggregate engine providers; filter by language)
7. ResolveTemplate         (stage-A parse: --template, or interactive picker)
8. HydrateTemplateOptions  (TemplateOptionHydrator: metadata → real Option<T>)
9. ReParseWithHydration    (stage-B parse of original argv)
10. ResolveFunctionName    (--name, else prompt; default from template)
11a. ValidateBundlePresence   (Node/Python; IExtensionBundleResolver)
11b. ValidateMinBundleVersion (Node/Python; DotNet: hive-provisioned check)
12. ApplyAsync             (dispatch by template.EngineId)
13. Render                 (NewCommandRenderer; --output plain|json)
```

- **Two-stage parse** → per-template options appear as native
  System.CommandLine options under `func new --template <id> --help`
  (`--auth-level`, `--route`, `--schedule`, `--queue-name`, `--connection`,
  `--file` for Python blueprints, `--namespace`/`--access-rights` for
  DotNet…). Every prompt IS an option; no `-p key=value` catch-all.
- `func new --list` / `-l` is the catalog surface (requires an init'd
  project; JSON envelope `{ stack, language, templates: [...] }`; plain output
  is NAME/TRIGGER/DESCRIPTION columns; `--help` renders an "Available
  templates" section from the same catalog; tab completion wired to it).
- **Engine identity is implicit and never user-visible** — currently derived
  from payload directory layout (`content/v2/` → V2;
  `content/dotnet-templates.json` → DotNet).
- Function-name validation: template-metadata validator authoritative,
  per-stack default fallback via `IProjectInitializer` (spec Q9).
- Dropped v4 behaviors (stay dropped): auto-init, positional template id,
  `--csx`, `--language`/`--profile`/`--offline` flags, programming-model
  knob + sniffs, `func templates list` verb, `func function new` aliases,
  `<trigger> help` syntax, `Template.Metadata.Extensions` NuGet installs.
- Telemetry: `cli.new.invoked` (stack, language, template id, engine id),
  `cli.new.duration`, `cli.new.outcome`, `cli.new.failure_kind`.

### 4.2 Contract as implemented (`src/Abstractions/Templates/`)

```csharp
public interface ITemplateEngineProvider
{
    string EngineId { get; }                       // EngineIds: "v2" | "dotnet"
    Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        TemplateListContext context, CancellationToken ct);
    Task<TemplateApplicationResult> ApplyAsync(
        NewContext context, ParseResult parseResult, CancellationToken ct);
}
```

Supporting types: `FunctionTemplateInfo` (Id, Stack, EngineId, DisplayName,
TriggerKind, DefaultFunctionName, Languages, `TemplateMetadata`),
`TemplateUserPrompt` (unified prompt shape: id, label, help, default, enum
entries, regex validators), `TemplateListContext(WorkingDirectory, Stack,
Language)`, `NewContext(WorkingDirectory, Template, FunctionName, Language,
Force)`, sealed `TemplateApplicationResult` (`Created(files)` /
`AlreadyExists(existing)` / `Failed(failure)`) with typed
`TemplateApplicationFailure`s (`NoTemplatesWorkloadForChannel`,
`MissingExtensionBundle`, `MinBundleVersionTooOld`, `WriteFailed`,
`InvalidPrompt`, `ProviderError`), `IInstalledTemplatesWorkloads`.

Registration: bare `AddSingleton<ITemplateEngineProvider, …>()` from each
engine csproj; orchestrator routes via `ITemplateEngineProviderRegistry` by
`EngineId`.

### 4.3 V2 engine as implemented (`src/Templates.V2/` — to be deleted)

- `V2Schema`: `NewTemplate { id, name, description, programmingModel,
  language, triggerType, category, categoryStyle, jobs[], actions[],
  files{} }`; `V2Job { name, type, inputs[], actions[] }`; `V2Action { name,
  type, filePath, fileContent, assignTo, source, continueOnError,
  createIfNotExists }`; `UserPromptDoc` (validators regex+errorText, enum).
- `V2TemplateEngine` action types (case-insensitive):
  `GetTemplateFileContent` (inline file → variable), `WriteToFile`
  (substitute `$(KEY)` + write), `AppendToFile` (substitute + **append to
  existing file**; error if missing and `createIfNotExists` false),
  `ShowMarkdownPreview` (no-op). Job types: `CreateNewApp`, `AppendToFile`
  (+ `CreateNewBlueprint` referenced in comments/Python content) — multi-job
  templates expose alternative flows (Python v2: create app file vs append
  function vs blueprint routing via `--file`).
- Node in-repo templates use only CreateNewApp + Get/Write (each function is
  a fresh `src/functions/<name>.<js|ts>` file).

### 4.4 DotNet engine as implemented (`src/Templates.DotNet/` — to be deleted)

`DotNetPayloadReader`/`DotNetSchema` (reads `dotnet-templates.json`),
`DotNetSourceReader` (`source.json`), `ItemTemplateHiveProvisioner`
(`dotnet new install … --debug:custom-hive`, sentinel, 5-min timeout,
**requires dotnet on PATH**), `IDotnetTemplateRunner` shell-out,
`DotNetTemplateProjection` → `FunctionTemplateInfo`.

### 4.5 Adjacent machinery the new design slots into (unchanged)

- Workload subsystem `src/Func/Workloads/{Catalog,Discovery,Install,Loading,
  Storage,Invocation}`; `src/Abstractions/Workloads/` (`Workload`,
  `FunctionsCliBuilder`, `CliWorkloadAttribute`).
- `Func.csproj`: **net10.0, self-contained, single-file publish** (not native
  AOT today; a `VerifySingleFilePublish` target guards against loose files).
  Already references `NuGet.Packaging`, `NuGet.Protocol`,
  `System.CommandLine`, `Spectre.Console`, `Microsoft.Extensions.*`, `Semver`.
- Profile system, `StackOptions` (`.func/config.json` binding via
  `StackOptionsSetup` — the single file-read path), `IExtensionBundleResolver`,
  `IInteractionService`, typed sealed failure idiom. (`TemplatesChannelMapper`,
  `TemplatesWorkloadManifestReader`, `InstalledTemplatesWorkloads` also
  exist on the branch but are **deleted by this change** — D16/D21.)

---

## 5. History: why the current design looks like it does (v4 → v5)

- v4 `func new` (`CreateFunctionAction`, 624 lines) had three engines
  (v1 Files-map, v2 jobs/actions, CSX/dotnet), programming-model sniffs,
  prompt loops, CDN fetching. v5 killed network-at-invocation, v1 templates,
  model knobs, auto-init, `--csx`.
- The V2 DSL exists because the **extension bundle** ships it
  (`StaticContent/v2`) for portal/IDE consumption; the CLI inherited it, then
  v5 forked its schema (CLI-owned). Node content is already hand-maintained
  in-repo. **The CLI is thus maintaining a bespoke engine and a bespoke
  corpus anyway** — the core argument for this change.
- The DotNet path already consumes `template.json` — via build-time
  down-projection + runtime shell-out, because in-proc hosting wasn't
  attempted at the time.

---

## 6. The Microsoft templating engine — deep dive

### 6.1 Packages

| Package | Role | Notes |
|---|---|---|
| `Microsoft.TemplateEngine.Abstractions` | All interfaces (`ITemplateEngineHost`, `ITemplateInfo`, `ITemplatePackageProvider`, `IMountPoint`, `IInstaller`, `IPostActionProcessor`, …) | |
| `Microsoft.TemplateEngine.Edge` | Hosting: `EngineEnvironmentSettings`, `DefaultTemplateEngineHost`, `TemplatePackageManager`, `TemplateCreator`, `TemplateConstraintManager`, built-in installers (NuGet, Folder), mount points (zip/nupkg, filesystem), bind sources (`host:`, `env:`) | Deps: `NuGet.Configuration`, `NuGet.Credentials`, `NuGet.Protocol` (func already ships NuGet.*) |
| `Microsoft.TemplateEngine.Orchestrator.RunnableProjects` | The **`template.json` generator**: parsing, symbols, content transforms | Register via `AddComponent`/`loadDefaultComponents`. No Newtonsoft dependency (STJ on .NETCoreApp) |
| `Microsoft.TemplateEngine.IDE` | `Bootstrapper` façade over Edge | Optional; func will likely use Edge directly for cache-path control |
| `Microsoft.TemplateEngine.Utils` | `DefaultTemplatePackageProvider`, `WellKnownSearchFilters`, physical/in-memory FS | |
| `Microsoft.TemplateSearch.Common` | NuGet.org template search | **Not needed** |

All multi-target `$(NetMinimum)/$(NetCurrent)/net472`;
**`IsAotCompatible=true`** on .NETCoreApp targets.

### 6.2 Hosting model

```
ITemplateEngineHost (Identifier + Version, ILogger plumbing,
 │        IPhysicalFileSystem (virtualizable), BuiltInComponents,
 │        TryGetHostParamDefault, FallbackHostTemplateConfigNames)
 ▼
EngineEnvironmentSettings (IEnvironment, IPathInfo, ComponentManager)
 │   default paths: ~/.templateengine/                    (global)
 │                  ~/.templateengine/<hostId>/           (host)
 │                  ~/.templateengine/<hostId>/<hostVer>/ (host version)
 ▼
TemplatePackageManager           TemplateCreator
 - aggregates ITemplatePackageProvider(s)   - InstantiateAsync (scaffold)
 - builds + persists template cache         - GetCreationEffectsAsync (dry run)
 - first scan expensive; keep instance      - InputDataSet for externally
   alive for process lifetime                 evaluated param conditions
TemplateConstraintManager — evaluates template-declared constraints
```

- **Host identity scopes settings/cache** per host id + version — a `func`
  host is isolated from `dotnet new`.
- `virtualizeConfiguration` (Bootstrapper) / `VirtualizePath` → in-memory
  settings: zero disk state but full rescan each process.
- `TryGetHostParamDefault` injects host parameter defaults (e.g.
  `HostIdentifier`); `host:`-prefixed **bind symbols** pull host values into
  content.
- **Host template config files:** `<hostIdentifier>.host.json` inside
  `.template.config/` (base name `.host.json`,
  `RunnableProjects.DirectoryBasedTemplate.HostTemplateFileConfigBaseName`),
  matched by host `Identifier` with `FallbackHostTemplateConfigNames` as
  additional accepted prefixes. ⇒ a func host (`Identifier = "func"`) reads
  `func.host.json`. **Correction (2026-07-28, D10):** the earlier assumption
  that func could inherit `dotnetcli.host.json` hints from the Functions
  item templates was falsified — the `Functions.Templates` repo ships no
  `dotnetcli.host.json` at all (only VS host files). Since we own that repo,
  `func.host.json` is contributed at the source for DotNet templates, same
  convention as Node/Python (design D9/D10).

### 6.3 Template package plumbing

- **`ITemplatePackageProvider`** (non-managed): yields template package
  locations (folder paths or `.nupkg` paths); engine scans/caches;
  `TemplatePackagesChanged` event triggers rescan. Precedents in `dotnet new`:
  `BuiltInTemplatePackageProvider` (SDK-shipped nupkgs),
  `OptionalWorkloadProvider` — direct analogue for a func provider
  enumerating installed templates workloads.
- **`IManagedTemplatePackageProvider`** + **`IInstaller`** (NuGet, Folder):
  the engine's own package management (what `dotnet new install` uses).
  **Func USES this since the 2026-07-30 pivot (D21)** — against a
  func-owned settings location, never the `dotnet new` global state.
  (Historical note: the pre-pivot design D5/D17 rejected it in favor of
  workload acquisition; D21 reversed that.)
- **Mount points:** filesystem mount (extracted folder) and zip mount
  (`.nupkg` consumed **directly**, no extraction/install step).
- **`IPrioritizedComponent`:** provider priority; within a provider, later
  packages win on duplicate template identity.

### 6.4 Instantiation surface

- `TemplateCreator.InstantiateAsync(ITemplateInfo, name, outputPath,
  parameters, …)` → `ITemplateCreationResult` (status, creation result with
  primary outputs + post actions).
- `GetCreationEffectsAsync` → **dry run** listing file operations
  (create/overwrite) → clean mapping onto `AlreadyExists` detection before
  any write.
- Statuses: `Success`, `CreateFailed`, `MissingMandatoryParam`,
  `InvalidParamValues`, `NotFound`, `CondtionsEvaluationMismatch` (sic), …
- Parameter surface (`ITemplateInfo.ParameterDefinitions`): name, datatype,
  default, choices (+descriptions), isRequired/isEnabled conditions,
  displayName, description — everything `TemplateOptionHydrator` needs,
  live, no build-time projection.

### 6.5 template.json capability surface

- **Identity:** `identity` (stable unique), `groupIdentity` (dedupes language
  variants), `shortName` (list), `name`, `description`, `author`,
  `classifications[]`, `tags { language, type: project|item, … }` (open
  dictionary), `precedence`, `defaultName`, `preferNameDirectory`,
  **`sourceName`** — the token replaced by `--name` in file **content and
  file/dir names** (with derived casing/safe-name forms).
- **Symbols:** `parameter` (string/choice/bool/int/float/hex/text;
  multi-choice; choice descriptions; `isRequired` incl. conditional;
  `isEnabled`), `derived`, `generated` (macros: guid, now, random, casing,
  coalesce, evaluate, join, regex, switch, port…), `computed`, `bind`
  (host:/env: sources), value `forms` (casing, safe_name, namespace…).
- **Content manipulation:** `sources[].modifiers[]`
  (`copyOnly`/`exclude`/`include`/`rename`, condition-capable);
  conditional-processing comment dialects per file type (`#if/#else/#endif`
  per-language comment syntaxes); custom operations per glob.
- **Output:** `primaryOutputs[]` (condition-capable) — feeds post actions and
  host reporting.
- **Post actions** (fixed registry by GUID; host implements the processors it
  supports; unsupported → `manualInstructions` text displayed):
  restore NuGet `210D431B…`, run script `3A7C4B45…`, open file `84C0DA21…`,
  add project reference `B17581D1…`, add to solution `D396686C…`, chmod
  `CB9A6CF3…`, display manual instructions `AC1156F7…`, **add property to
  existing JSON file** `695A3659…`. Hosts may define their own GUIDs +
  processors → func can ship Functions-specific post actions.
- **Constraints:** template-declared, host-evaluated (`ITemplateConstraint`
  components; custom ones possible — candidate future home for min-bundle).
- **Localization:** `.template.config/localize/templatestrings.<lang>.json`
  sidecars — engine-native i18n for names/descriptions/choices. Strictly
  better than both current formats' story.
- **Host files:** `.template.config/<hostId>.host.json` (§6.2) — CLI aliases,
  hidden params, ordering.

### 6.6 What the engine does NOT do natively

1. **No "append to existing file" primitive.** The generator materializes a
   source tree; it creates/overwrites files. Python v2's "add this function
   to the user's existing `function_app.py`" has no built-in equivalent.
   The "add JSON property" post action is upstream precedent for
   targeted-existing-file mutation via post action. (→ design §2.5.)
2. **No interactive prompting** — host's job (func's parser/picker owns it).
3. **No regex validators on parameters** — V2 `UserPrompt.validators`
   (regex + errorText) has no symbol-level equivalent (constraints validate
   *template usability*, not parameter values). (→ design OQ-8.)
4. **Components ship as code** — macros/post-action processors/constraints
   are host-registered. Historic engine versions could scan assemblies out of
   template packages; func must ensure **no assembly loading from template
   content** (security; design SP-4).

### 6.7 HISTORICAL — Registry → mounts → cache → query (pre-pivot reconciliation model)

> **Superseded by D21**: there is no workload registry involvement, no
> provider yield rule, and no provenance map anymore — the engine's own
> package store + cache are the truth, maintained by its install/uninstall/
> update operations. The engine-internal cache mechanics described at the
> end of this section (`MountPointsInfo`, format `Version`, `Locale`,
> rebuild triggers) **remain accurate and relevant** — they're properties
> of the engine, not of the retired func layering.

Four layers, two narrowing mechanisms (versions narrow at *mount* time;
stack/channel/language narrow at *query* time):

1. **Workload registry** (`workloads.json`) — CLI-owned truth; rows for every
   installed `(packageId, version)` including side-by-side versions.
2. **Mounts** — `WorkloadTemplatePackageProvider` projects the registry down
   via the **yield rule** (design.md §2.3): one mount per
   `(packageId, channel)` = highest version in that channel. Older versions
   stay on disk for rollback but are never mounted, never scanned, never
   cached (avoids template-identity collisions and last-yielded-wins
   accidents). A provenance map records
   `mountUri → (packageId, version, stack, channel)`.
3. **Template cache** (`<workload-home>/template-engine/func/<cli-ver>/
   templatecache.json`) — derived metadata for every template in every
   mounted package, all stacks and channels together, keyed by mount.
   Reconciliation, not subscription: on each engine use the
   `TemplatePackageManager` diffs the provider's current mount list against
   what the cache was built from; unchanged → fast path, changed → rescan
   affected mounts and rewrite (lazy, per design D7). A stale cache file
   between invocations is harmless — reconciliation runs before queries.
   **How the cache knows what it was built from** (per
   `TemplateCache.cs` / `TemplatePackageManager.UpdateTemplateCacheAsync`):
   `templatecache.json` persists `MountPointsInfo`
   (`mountPointUri → LastChangeTime`), a cache-format `Version`, and the
   build `Locale`. Rebuild triggers, in order: unparsable cache / format
   version mismatch / locale mismatch → full rebuild; per-mount newer
   `LastChangeTime` → rescan that package only; current URI set ≠ cached
   URI set → rebuild (catches removals). Because func mount URIs embed the
   workload version directory (`…/<packageId>/<version>/…`), every
   install/uninstall/update changes the URI set — the cheap set diff is
   the primary detector; timestamps only matter for in-place payload edits.
   Implementation note: `WorkloadTemplatePackageProvider` must report honest
   `LastChangeTime` values for yielded paths.
4. **Query** — `MsEngineProvider` filters all cached entries by the
   project's selection (stack from `.func/config.json`, channel-matched
   package from `host.json` bundle id, language) via the provenance map.

Consequences: uninstalling the highest version of a package flips the yield
to the next-highest; the next `func new` rebuilds and the older version's
templates surface (rollback just works). Deleting the cache directory is
always safe — worst case is one rebuild scan.

### 6.8 How `dotnet new` hosts the engine (precedent)

- Host id `dotnetcli`, versioned with the SDK; settings + template cache
  under `~/.templateengine/dotnetcli/v<sdk-version>/`.
- Built-in templates via a non-managed provider yielding SDK-shipped nupkg
  paths; optional-workload templates via a second provider; user installs
  via the global managed provider + NuGet/Folder installers.
- Template matching, parameter → option projection
  (`CliTemplateParameter.GetOption`), tab completion, `--help` rendering all
  live host-side (this is exactly what func's `dotnet-templates.json`
  hydration mimicked at build time — now replaced by live reads).

---

## 7. Historic design alternatives considered and rejected

Captured so future agents don't re-litigate:

- **Engine as `kind: workload` package** (ALC-loaded, revs independently):
  rejected (design D1) — breaks under future native-AOT `func` (AOT can't
  ALC-load managed assemblies), adds per-invocation ALC + engine init cost
  and contract-skew management. Still rejected post-pivot.
- **Phased stack migration** (DotNet first, Python later): rejected (D2) —
  one design covering all stacks; Python append risk handled up front via
  spike SP-2. Still stands.
- **Workload-based template acquisition** (templates as `kind: content`
  workloads; engine package management unused): this was the *chosen*
  design (D5, reaffirmed D17 in the jviau reconciliation) until the
  **2026-07-30 pivot reversed it (D21)** — engine-managed acquisition won.
  History matters here: don't re-argue either direction without reading
  D5 → D17 → D21.
- **Per-stack payload wrapping mix** (D3: DotNet embeds upstream nupkg in
  a workload; Node/Python ship folder trees in workloads): mooted by D21 —
  packages are now standard template packages, no wrapping at all.
- **Channel axis + sidecar manifest gating**: rejected in the jviau
  reconciliation (D16) in favor of per-template `template.json`
  constraints. Still stands post-pivot.
- **`func templates` command tree**: rejected (D26) — lifecycle/search
  fold into `func new`; project templates into `func init`.

---

## 8. Metadata mapping — old concepts → template.json

| func concept today | template.json home | Notes |
|---|---|---|
| Template id (`HttpTrigger-JavaScript`) | `identity` (stable) + `shortName` list — **unified upstream-style primary id** (`http`, `timer`, …) per D19, with `HttpTrigger`-style and legacy suffixed forms as aliases | `--template` matches any shortName (case-insensitive) |
| Stack (node/python/dotnet) | custom tag `tags: { "azfunc-stack": "node" }` | Post-pivot the tag is authoritative (no workload provenance map anymore, D21) |
| Language (JS/TS, C#/F#, python) | `tags.language` + `groupIdentity` for variant dedupe | Native; catalog dedupe on groupIdentity carries over |
| TriggerKind (http/timer/queue/…) | custom tag `azfunc-trigger` (preferred) or `classifications` | Drives the TRIGGER column + picker grouping |
| DisplayName / Description | `name` / `description` (+ localization sidecars) | |
| DefaultFunctionName | `defaultName` | `sourceName` replacement renames files/content to `--name` |
| UserPrompt id/label/enum/default/required | `symbols` type `parameter` (choices w/ descriptions, defaultValue, isRequired) | Hydrator reads `ITemplateInfo.ParameterDefinitions` live |
| Regex validators (V2) | `func.host.json` `symbolInfo[].validator` (design D9) | Engine-inert; hydrator-applied. Today's only real validator in the Node corpus is functionName |
| CLI alias overrides (`--auth-level` for `AccessRights`) | `func.host.json` `symbolInfo[].longName` — added at the source for all stacks incl. DotNet (design D10; no `dotnetcli.host.json` exists upstream) | |
| Hidden templates (`V2HiddenTemplates`) | omit from package, or `hidden` flag in host file | |
| i18n resources (`Resources*.json`) | `localize/templatestrings.<lang>.json` | |
| minBundleVersion / channel | **per-template `template.json` `constraints`** (`func-extension-bundle` custom constraint; D16) — sidecar manifest and channel axis deleted | Unmet → hidden/restricted with call-to-action; init surfaces evaluate vs latest available bundle (D30) |
| `programmingModel` field | gone (already dropped in v5) | |
| Inline `files` map | real files in template content dir | Diffable; conditional processing becomes available |
| `$(FUNCTION_NAME_INPUT)` token | `sourceName` mechanism | Author content with placeholder identifier; engine substitutes + renames |
| Python `--file` blueprint routing | `parameter` symbol (e.g. `AppFile`) consumed by func append post action | design §2.5 |

---

## 9. Worked authoring example — Python HttpTrigger (design D13)

Canonical reference for the append-flow template shape. Post-pivot these
files live in the standard template package
`Microsoft.Azure.Functions.Templates.Python` (D28) at
`HttpTrigger/.template.config/…` — no workload payload wrapper. A
`constraints` block (D16) would sit alongside `symbols` when the trigger
has a bundle requirement. Three files:

`templates/HttpTrigger/.template.config/template.json`:

```jsonc
{
  "author": "Microsoft",
  "classifications": ["Azure Function", "Trigger", "Http"],
  "name": "HTTP trigger",
  "description": "Function triggered by HTTP requests.",
  "identity": "AzureFunctions.Python.HttpTrigger.1.0",
  "groupIdentity": "AzureFunctions.Python.HttpTrigger",
  "shortName": ["http", "HttpTrigger", "HttpTrigger-Python"], // D19: unified id + aliases
  "tags": { "language": "python", "type": "item",
            "azfunc-stack": "python", "azfunc-trigger": "http" },
  "sourceName": "HttpTriggerFunc",                           // --name replaces this token
  "defaultName": "http_trigger",
  "symbols": {
    "AuthLevel": { "type": "parameter", "datatype": "choice",
      "choices": [ { "choice": "FUNCTION" }, { "choice": "ANONYMOUS" }, { "choice": "ADMIN" } ],
      "defaultValue": "FUNCTION", "replaces": "AUTH_LEVEL_VALUE",
      "description": "Authorization level for the HTTP endpoint." },
    "Route": { "type": "parameter", "datatype": "string",
      "defaultValue": "http_trigger", "replaces": "ROUTE_VALUE",
      "description": "Route for the HTTP endpoint." },
    "AppFile": { "type": "parameter", "datatype": "string",   // ← becomes --file
      "defaultValue": "function_app.py",
      "description": "File to add the function to. A new file is created as a blueprint." },
    "AppObject": { "type": "parameter", "datatype": "string", // ← CLI-driven, hidden
      "defaultValue": "app", "replaces": "APP_OBJECT" }
  },
  "primaryOutputs": [ { "path": "__snippet__.py" } ],
  "postActions": [ {
    "actionId": "<func-append-GUID>",
    "description": "Add the function to the target app or blueprint file.",
    "args": { "targetFileParam": "AppFile", "appObjectParam": "AppObject",
              "deleteStagedFile": "true" },
    "manualInstructions": [
      { "text": "Copy the contents of __snippet__.py into your function_app.py." } ],
    "continueOnError": "false"
  } ]
}
```

`func.host.json`:

```jsonc
{
  "$schema": "https://aka.ms/func-cli/func-host-json/v1/schema.json",
  "symbolInfo": [
    { "id": "AuthLevel", "longName": "auth-level" },
    { "id": "Route",     "longName": "route" },
    { "id": "AppFile",   "longName": "file" },
    { "id": "AppObject", "isHidden": true }
  ],
  "functionName": { "validator": {
    "expression": "^[a-zA-Z][a-zA-Z0-9_]{0,126}[a-zA-Z0-9]$",
    "errorText": "Function names must start with a letter and be at most 128 characters." } }
}
```

`__snippet__.py`:

```python
@APP_OBJECT.route(route="ROUTE_VALUE", auth_level=func.AuthLevel.AUTH_LEVEL_VALUE)
def HttpTriggerFunc(req: func.HttpRequest) -> func.HttpResponse:
    logging.info("Python HTTP trigger function processed a request.")
    ...
```

Key semantics:

- **`--file` is not hardcoded** — it is the `func.host.json` alias of the
  `AppFile` symbol. Templates without the symbol never hydrate the option;
  passing `--file` to them is a standard unrecognized-option parse error.
- **Flow logic keys off the post action, not the stack**: the provider
  recognizes the func append actionId, reads `targetFileParam` /
  `appObjectParam`, resolves the target (explicit `--file` or default),
  determines create-vs-append and app-vs-blueprint, and injects `AppObject`
  programmatically (hidden = hidden from users, not from the CLI). Any
  stack can adopt append semantics by declaring the same shape.
- **Graceful degradation**: foreign engine hosts (VS, dotnet new) lacking
  the func processor scaffold `__snippet__.py` and print the
  `manualInstructions` text instead of failing.
- Blueprint is **not a separate catalog row** — one template per trigger;
  `--file` routes the flow (matches v4's four-jobs-in-one-entry surface).

## 10. The pivot architecture (2026-07-30) — authoritative summary

Full detail: design.md §2A + D21–D31. The shape an implementer should
hold in their head:

- **Packages:** standard engine template packages (templates at package
  root under `<Name>/.template.config/`), identified by NuGet package
  types `FuncItemTemplates` (→ `func new`) and `FuncAppTemplates`
  (→ `func init`); one package may declare both. Official ids (D28):
  .NET keeps upstream `Microsoft.Azure.Functions.Worker.ItemTemplates` /
  `…Worker.ProjectTemplates` (func types added, `Template` type kept);
  Node/Python get one dual-type `Microsoft.Azure.Functions.Templates.
  <Stack>` each (items + `Empty` project template). Plain semver; gating
  via per-template constraints (D16), never channels/sidecars.
- **Acquisition:** engine `TemplatePackageManager` into the func-owned
  hive under the CLI home; engine package store + template cache are the
  installed-state truth. No workload registry involvement. Rollback =
  install an older version explicitly (no workload-style SxS).
- **Command surface (D26):** `func new --search/--install/--uninstall/
  --update` (any template package; these modes **bypass** the project/
  profile gates per D30) + `func init --template` and the wizard's
  project-template step. No `func templates` tree.
- **First run (D27):** `func init` auto-installs the resolved stack's
  official packages when missing (offline → actionable error);
  `func setup` may pre-install; `func new` is hint-only, never installs.
- **Search (D22/D29):** func discovery pipeline (based on
  TemplateDiscovery, §11) scans feeds by package type and publishes the
  index (`NuGetTemplateSearchInfoVer2.json`) behind an `aka.ms` vanity
  URI → Functions CDN; daily incremental runs + on-demand trigger; CLI
  consumes via search coordinator with local-file override; `--source`
  queries a feed's search API live (local/private feeds).
- **`func init` (D23/D31):** `IProjectInitializer` is a thin metadata
  contract (stack id, worker-runtime aliases, display name, languages/
  aliases, Q9 default validator, official package ids). Init core
  orchestrates; per-stack init options come from `Empty`-template symbols
  hydrated like `func new` options; `.func/config.json` stays
  init-authored; the DotNet `dotnet new func` init shell-out is deleted;
  stack workload still required at init; a stack without an official
  `Empty` package → actionable error. Init-surface constraint context =
  latest available bundle (D30); wizard degrades to installed-only when
  offline.
- **Unchanged by the pivot:** `func new` scaffold pipeline (project
  mandate, two-stage parse, hydration, picker, dry-run/AlreadyExists),
  Python append design (D13 + staging isolation), post-action allowlist +
  add-reference processor + `msbuild:` bind source (D6/D11), constraint
  gating (D16), unified ids (D19), no provider seam (D20), `func.host.json`
  (D9/D10), authoring home plan (D12).

## 11. Search machinery facts (TemplateDiscovery + TemplateSearch.Common)

From the sources listed in §1:

- **Discovery tool** (`Microsoft.TemplateSearch.TemplateDiscovery`):
  queries a feed's NuGet **search API** with configurable queries — the
  dotnet run uses `packageType=Template` and `q=template`
  (`NuGetPackSourceCheckerFactory.SupportedProviders`); func substitutes
  `packageType=FuncItemTemplates` / `packageType=FuncAppTemplates`. Pages
  results (default 100/page), prefilters candidates for `template.json`
  presence, downloads and scans them with the real engine, and writes to
  `SearchCache/`: `NuGetTemplateSearchInfoVer2.json` (current format),
  the legacy v1 file, and `nonTemplatePacks.json` (known non-template
  packages, used to skip on later runs). `--diff` (default true) skips
  unchanged package versions against a previous cache; `--packagesPath`
  scans pre-downloaded packages (⇒ local-feed support); stable-only by
  default (`--allowPreviewPacks` opts in). `IAdditionalDataProducer`
  (e.g. `CliHostDataProducer`) embeds per-host extra data in the cache —
  the hook for func-specific metadata if ever needed.
- **Consumer** (`Microsoft.TemplateSearch.Common`):
  `NuGetMetadataSearchProvider` downloads the cache from configured URIs
  (dotnet uses fwlink redirects — precedent for our aka.ms indirection),
  caches locally, honors a local-file override and a
  `UseLocalSearchFileIfPresent`-style env toggle;
  `TemplateSearchCoordinator` + filters power the search UX. Cache model:
  packages → templates with identity/shortNames/tags/etc.

### 11.1 Source-verified detail (2026-07-30, read from the repo)

Ground truth for SP-7 / tasks §3.5, read directly from
`Tools\Microsoft.TemplateSearch.TemplateDiscovery` and
`Microsoft.TemplateSearch.Common`:

- **Query set is a hard-coded dictionary** keyed by an internal
  `SupportedQueries` enum (`NuGetPackSourceCheckerFactory`):
  `PackageTypeQuery → "packageType=Template"`,
  `TemplateQuery → "q=template"`. The feed is also a constant
  (`NuGetPackProvider.NuGetOrgFeed = https://api.nuget.org/v3/index.json`);
  the search endpoint is resolved from that feed's service index
  (`SearchQueryService`) and formatted as
  `{searchUri}?{query}&skip={0}&take={1}&prerelease={includePreview}&semVerLevel=2.0.0`.
  ⇒ adding `packageType=FuncItemTemplates` / `FuncAppTemplates` requires
  **either an upstream contribution (make queries + feed configurable) or a
  func-owned fork** — everything else (download, scan, prefilter, diff,
  cache write) is reusable as is. **Open implementation choice, not yet
  decided.**
- **NuGet search API ceiling:** `skip` caps at 3000 and page size at 1000,
  so a single query can retrieve at most **4000 packages** (the tool warns
  and truncates beyond that). `packageType=Template` runs near the ceiling;
  func's package-type queries return a tiny set — a real argument for
  type-scoped indexing.
- **Command surface** (`template-discovery`): `--basePath` (required),
  `--queries` (provider list; omitted = all), `--diff` (**default true**),
  `--diff-override-cache` / `--diff-override-non-packages` (local files
  instead of downloads), `--packagesPath` (scan pre-downloaded packages;
  switches the factory to `TestPackCheckerFactory`, no NuGet.org access —
  the offline/local-feed path), `--allowPreviewPacks` (default stable
  only), `--pageSize` (default 100), `--onePage`, `--savePacks`,
  `--noTemplateJsonFilter`, `--test` (asserts over generated metadata),
  `--verbose`.
- **Three prefilters:** `TemplateJsonExistencePackFilter` (no
  `template.json` → drop), `SkipTemplatePacksFilter` (skip-list from
  `nonTemplatePacks.json`), and `FilterNonMicrosoftAuthors` — which is an
  **anti-spoofing** filter, not a Microsoft-only gate: packages whose id
  starts with the reserved `Microsoft.` prefix pass; any other package
  containing a `template.json` with `author` matching "microsoft" is
  filtered out.
- **Outputs** (`PackCheckResultReportWriter`), all under `SearchCache/`:
  `NuGetTemplateSearchInfoVer2.json` (current, `Version` `"2.0"`),
  `NuGetTemplateSearchInfo.json` (legacy v1), `nonTemplatePacks.json`.
  Supported cache versions read by the consumer: `1.0.0.0`, `1.0.0.3`,
  `2.0`.
- **Index schema** (property names come from `nameof`): package rows carry
  `Name`, `Version`, `TotalDownloads`, `Owners`, `Reserved`,
  `Description`, `IconUrl`, `Templates[]`; each template carries
  `Identity`, `GroupIdentity`, `Precedence`, `Name`, `ShortNameList`,
  `Author`, `Description`, `ThirdPartyNotices`, `Classifications`,
  `TagsCollection`, `Parameters[]` (`Name`, `DataType`, `Description`,
  `DefaultIfOptionWithoutValue`, `Choices`), `BaselineInfo`,
  `PostActions`. ⇒ `azfunc-stack` / `azfunc-trigger` tags ride along in
  `TagsCollection`, so index results can be stack-filtered like the local
  catalog.
- **Diff mode inputs:** the previous cache is fetched from
  `https://aka.ms/dotnet/templating/searchcacheurl` (source comment: the
  aka.ms link should point at "whatever the future absolute URL for the
  JSON file is") and the skip-list from
  `https://dotnettemplating.blob.core.windows.net/search/nonTemplatePacks.json`
  — direct precedent for D29's vanity-URI indirection.
- **Consumer mechanics** (`NuGetMetadataSearchProvider`): local cache file
  `nugetTemplateSearchInfo.json` in `Paths.HostVersionSettingsDir` (⇒ the
  func hive); re-download only when the cached copy is older than
  **1 hour**; `ETag` persisted in a `.etag` sibling and sent as
  `If-None-Match`, with `304` just touching the timestamp; on failure it
  falls back to the stale local copy with a warning and throws only when
  no copy exists. Env vars: `DOTNET_NEW_SEARCH_FILE_OVERRIDE` (explicit
  path) and `DOTNET_NEW_LOCAL_SEARCH_FILE_ONLY` (never download) — func
  needs its own equivalents. The URI list is injectable only through an
  **`internal` test constructor**, so pointing the provider at the func
  index is the second place a small upstream change beats a fork.

## 12. Checkpoints / session log

- **CP-2026-07-28-a** — Context capture complete: three specs read in full;
  branch implementation inventoried (`Abstractions/Templates`,
  `Func/Templates`, `Templates.V2`, `Templates.DotNet`,
  `Workloads/Templates/*`, workload subsystem, Func.csproj posture); MS
  engine architecture, template.json surface, post-action registry, hosting
  and host-file mechanics studied; `dotnet new` host precedent mapped.
- **CP-2026-07-28-b** — Direction decided with the user (design D1–D4):
  CLI-internal engine, all stacks, per-stack payload mix, doc in this
  OpenSpec change. Architecture drafted; open questions narrowed to
  OQ-8/10/11/12/13/14; spikes SP-1…SP-5 defined.
- **CP-2026-07-28-c** — Document split: lean `design.md` (decisions,
  architecture, risks, open questions, decision log) + this `context.md`
  (background for parallel contributors). Proposed next step on record:
  combined walking-skeleton spike (SP-1 + SP-2 + slice of SP-5) before
  drafting `proposal.md`.
- **CP-2026-07-28-d** — **User-journey walkthrough complete (S1–S9,
  design.md §6).** All open questions resolved into decisions D7–D15
  (lazy cache/no installer special-casing; short template ids + legacy
  aliases; `func.host.json` validators/aliases; hints-at-the-source for
  DotNet; add-reference processor + `msbuild:` bind source; core-tools
  authoring now → `Functions.Templates` later; Python flow matrix;
  hive left on disk; minimal sibling manifest). Ground-truthing against
  `Functions.Templates` falsified the dotnetcli.host.json-inheritance
  assumption and surfaced the load-bearing post action + bind symbol.
  Spike Task 0 (tasks.md) remains the implementation gate; SP-2/SP-3
  narrowed to mechanics/concurrency only. OQ-17 (PowerShell templates)
  parked as follow-up-change scope. **Next:** draft `proposal.md` +
  `specs/` deltas, then run Task 0 before implementation.
- **CP-2026-07-28-e** — **Reconciliation with
  `func-universal-template-engine`** (jviau's parallel OpenSpec change on
  `u/jviau/vnext/templates-spec`). Identical engine core independently
  chosen by both. Four forks ruled with the user (design.md §6
  reconciliation table): acquisition stays workload-based (D17); gating
  moves to per-template `template.json` constraints, deleting the channel
  axis / sidecar manifest / Node pack-time subsetting (D16); project
  mandate and no-`--language` stand, their empty-dir mode recorded as
  future product discussion (D18); template ids unified upstream-style
  across stacks with legacy aliases (D19, amends D8); the
  `ITemplateEngineProvider` seam is deleted (D20). Proposal and all three
  spec deltas revised accordingly; change validates. Their change still
  lacks: Python append design, post-action allowlist/add-reference
  processor, `msbuild:` bind source, `func.host.json`, cache decisions —
  merge conversation with jviau should bring those from here.
- **CP-2026-07-30-f** — **Pivot: templates leave the workload system**
  (user decision; design D21–D27, §2A). Engine `TemplatePackageManager`
  owns acquisition into the func hive; engine cache = installed truth; no
  workload registry/`workload.json`/`FuncCliWorkload` for templates.
  Package types: `FuncItemTemplates` / `FuncAppTemplates`. Command
  surface folds into `func new` (`--search/--install/--uninstall/
  --update`) and `func init` (`--template` + wizard step). New elements:
  **template search** via a func-owned discovery service based on
  `Microsoft.TemplateSearch.TemplateDiscovery` (feed scan by package
  type → engine scan → `NuGetTemplateSearchInfoVer2.json` index;
  incremental via `--diff` + `nonTemplatePacks.json`; consumer =
  search-coordinator provider with index URI + local override + direct
  `--source` feed queries); **project templates** in init (per-stack
  `Empty` matching today's output; stack-filtered selection;
  auto-install of official packages at init, `func setup` pre-install;
  `func new` stays hint-only and item-only). Offline = engine cache;
  explicit update command semantics. Capability set now: engine-host,
  template-packages (renamed from templates-workload-payload),
  scaffolding, template-search, project-templates. This partially
  overrides the fork-1 reconciliation ruling (D17) — templates now match
  jviau's acquisition position; the workload-vs-engine acquisition
  debate is settled in favor of the engine. Open: OQ-22 (official
  package ids, index hosting/cadence/ownership). New spikes SP-6/7/8 in
  tasks.md.
- **CP-2026-07-30-i** — **Task 0 gating spike run — GO** (design.md §6B,
  D32; scratchpad `spike/`). Validated in-proc hosting + engine-managed
  acquisition into a relocated func hive (isolation needs a custom
  `globalSettingsDir`, not just host id), the full Python append flow set
  via **host-side** post-action dispatch (no engine `IPostActionProcessor`
  — refines §2.6), staging isolation, real upstream `Worker.ItemTemplates`
  loading 36 templates verbatim (D28; `http` params include `AccessRights`
  → confirms D9 aliasing), and single-file publish (engine libs 0.84 MB,
  correcting the +2–3 MB estimate). Remaining spikes: SP-3 concurrency,
  SP-4 adversarial security, SP-7 discovery, SP-8 project-template/init,
  constraint eval. Next: expand tasks.md §3 into the implementation
  checklist and begin with deletions.
- **CP-2026-07-30-g** — OQ-22 resolved (D28 ids: .NET keeps upstream
  `Worker.ItemTemplates`/`Worker.ProjectTemplates` + func package types;
  Node/Python dual-type `Microsoft.Azure.Functions.Templates.<Stack>`;
  D29 hosting: aka.ms → Functions CDN, daily incremental pipeline).
  E2E sanity audit ran: D30 completion rules (lifecycle/search modes
  bypass project gate; init-scoped latest-bundle constraint context;
  offline wizard degradation), stale §2.4/§6-S1/S8 sections banner'd,
  migration inventory + proposal reconciled, SP-1 merged into SP-6.
  OQ-23 resolved as **D31**: `IProjectInitializer` becomes a thin
  stack-metadata contract (id, aliases, display name, languages, Q9
  validator, official package ids); init core orchestrates; per-stack
  init options become Empty-template symbols (one hydration mechanism
  across init/new); the DotNet `dotnet new func` init shell-out is
  deleted. Remaining open: OQ-17 (PowerShell) only; gates = spikes
  SP-2..8 (SP-1 folded into SP-6).

- **CP-2026-07-30-j** — **tasks.md §3 expanded into the implementation
  checklist** (69 tasks, 3 done). Phases: 3.1 demolition (V2 + DotNet
  engines, provider seam/registry/`EngineIds`, channel + sidecar
  machinery, templates-workload projects/targets/scripts/pipelines) →
  3.2 `src/Templates.Engine` host (relocated-hive `globalSettingsDir`,
  acquisition service, SP-3 locking, cache rules, `func-extension-bundle`
  constraint, host-side post-action dispatcher + append processor,
  `msbuild:` bind source, `func.host.json` reader, catalog + scaffolding
  services, SP-4 adversarial tests) → 3.3 `func new` rewiring (hydrator
  source switch, shortName resolution, constraint CTA, lifecycle/search
  flags with gate bypass) → then parallel 3.4 packages/corpus, 3.5 search
  (SP-7 first), 3.6 init/project templates (SP-8 first) → 3.7 validation
  and docs; §4 cross-repo/human track, §5 pre-archive. Two placement
  rulings recorded as **D33**. Flagged for a user ruling during 3.1.7:
  whether `engineId`/`requiresExtensionBundle`/`minBundleVersion` are
  dropped from the `func new --list` JSON envelope or emitted as
  constants. Note: the Task 0 spike scratchpad is no longer on disk —
  design.md §6B is the surviving record of its results.

- **CP-2026-07-30-k** — **Implementation landed end to end (phases 3.1-3.3 + a 3.4 slice).**
  Executed by six parallel agents. Deleted: `Templates.V2`, `Templates.DotNet`,
  the `ITemplateEngineProvider` seam/registry/`EngineIds`, channel+sidecar
  machinery, `src/Workloads/Templates/*` and their targets/scripts/pipelines
  (Node V2 corpus snapshotted to `corpus-snapshot/node-v2/`). Added
  `src/Templates.Engine` (host id `func`, relocated-hive `IPathInfo`,
  session, acquisition via `TemplatePackageManager` + two-layer cross-process
  hive lock, catalog, scaffolder, host-side post-action dispatcher with the
  3-GUID allowlist, `func-extension-bundle` constraint, `msbuild:` bind
  source, `func.host.json` reader) and `src/Templates/{Node,Python}` sample
  packages + `eng/scripts/build-local-template-feed.ps1`.
  **Verified independently:** Release build 0/0 under `CI=true`; 1342 tests
  pass; real CLI in a hermetic `FUNC_CLI_HOME`: install from local feed →
  `func init` → `--list` → Python append (bound to `app`, `--auth-level`
  hydrated from `func.host.json`) → blueprint via `--file` (registration
  instructions printed, `function_app.py` byte-identical) → duplicate guard
  rejects → `~/.templateengine` provably untouched.
  **Defects found + fixed during verification:** (a) `DeployForDebug` gated on
  `PackageType`, which is empty at Build time — no workload ever deployed,
  making project-gated E2E unreachable (pre-existing infra bug); (b) the
  renderer silently dropped `Created.Messages`, so blueprint registration
  instructions never reached the user; (c) constraint-restricted templates
  surfaced as bare "unknown template" — added `IFuncTemplateCatalog
  .FindRestrictedAsync` so the CTA renders (spec gap); (d) a failed append
  deleted the staged snippet its own error message pointed at (cross-agent
  seam, spec scenario "failed append orphans nothing"); (e) append rewrote
  the whole target file, flipping CRLF→LF and stripping BOM.
  **Not yet implemented:** phase 3.5 search (the discovery index client is a
  `--search` stub), phase 3.6 `func init` project-template step + D31 thin
  `IProjectInitializer`, 3.4.2 V2→template.json converter for the full 33-template
  Node corpus (samples were authored fresh), 3.4.6 parity harness, 3.4.7 the
  upstream `Functions.Templates` package-type PR, 3.2.15 SP-4 adversarial tests.
  Envelope-field ruling (3.1.7) still pending: `engineId` now emits null,
  `requiresExtensionBundle`/`minBundleVersion` still emit real values.

- **CP-2026-07-31-l** — **Team review feedback captured** in
  `DESIGN-FEEDBACK-2026-07-31.md` (not yet ratified into design.md §7):
  (1) collapse `FuncItemTemplates`/`FuncAppTemplates` into a **single**
  `FuncAppTemplates` type — team-decided, proposed D34, superseding the
  package-type half of D26 + the type assignments in D28; enabling fact:
  item-vs-project filtering already keys off per-template `tags.type`
  (`FuncTemplateTags`), so the package type is only a feed-level discovery
  filter and collapsing it is ~34 refs across 14 files with no CLI-layer
  loss. (2) OQ-25 project-template catalog/filter UX (the index already
  carries the needed tags). (3) doc gap: init's stack/language list is
  bounded by *installed workloads* + OQ-26 missing-stack CTA. (4) change
  request: `func new`'s "Created function 'x'" message
  (`NewCommandRenderer.cs:158`) overstates — may be many artifacts, or none,
  or an append-only edit. (5) OQ-27 init × profiles. (6) OQ-28 stack/language
  version compatibility — proposed as a `func-stack` constraint mirroring the
  proven `func-extension-bundle` one. (7) OQ-29 a GitHub-quickstart →
  project-template packaging pipeline (a *different* source adapter from the
  feed-based `tools/TemplateDiscovery`; suggested as its own change).
  Time-sensitive: (1) before the upstream 3.4.7 PR, (6) before the corpus
  conversion. Tracked as tasks.md 5.0.
