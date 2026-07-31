# Design — adopt-ms-template-engine

> Adopt the Microsoft templating engine (`dotnet/templating`, the engine
> behind `dotnet new`) as the **single CLI-internal engine** behind
> `func new`, consuming `template.json`-format templates delivered by
> per-stack **content workloads**. Replaces the bespoke V2 jobs/actions
> engine and the `dotnet new` shell-out.
>
> **Background, prior art, and code inventory live in
> [`context.md`](./context.md)** — read that first if you're new to this
> change. This file holds only decisions, the target architecture, risks,
> and open questions.

---

## 1. Decisions at a glance

| # | Decision | One-line rationale |
|---|---|---|
| D1 | Engine is **CLI-internal** (new `Templates.Engine` csproj; no engine workload) | AOT-viable, no new public contract, fastest startup; templates still rev independently |
| D2 | **All stacks** move to the engine in one design; V2 engine + dotnet shell-out deleted | One engine/one format is the point; Python append risk handled up front (§2.5, SP-2) |
| D3 | Templates payload is a **per-stack mix**: DotNet embeds the pinned upstream nupkg; Node/Python ship authored `.template.config` folder trees | Upstream provenance for DotNet; diffable authored content for Node/Python |
| D4 | This OpenSpec change is the canonical home of the design | Flows into propose→apply→archive |
| D5 | The engine's **own package management is not used** — a non-managed provider over installed workloads; cache under the workload home | `func workload` is the single package manager; offline + deterministic |
| D6 | **Post-action allowlist**: func append processor + manual-instructions only; no run-script, no restore, no assembly scanning from packages | Content workloads must never execute code |

Full decision log with dates and supersedes-links: §7.

Key wins this buys (details in context.md §5–§6):

- Kills the custom V2 DSL and the CLI's bespoke template-schema ownership.
- Kills the `dotnet`-on-PATH requirement, the template hive, and the
  build-time `dotnet-templates.json` down-projection for .NET.
- Gains engine-native conditions, symbols, localization sidecars, dry-run,
  and host-config files (`func.host.json`; upstream `dotnetcli.host.json`
  hints inherited via the fallback-names mechanism).

---

## 2. Architecture

### 2.1 Component map

```
Func                       src/Func/                       CLI executable
 ├── Commands/NewCommand.cs                                unchanged surface
 ├── Templates/                                            orchestrator (pipeline steps 1–11, 13)
 │     channel + minBundle selection policy stays here, upstream of the engine
 └── (project refs) ──► Templates.Engine  (NEW — replaces Templates.V2 + Templates.DotNet)

Templates.Engine           src/Templates.Engine/           NEW CLI-internal csproj
 ├── FuncTemplateEngineHost        : ITemplateEngineHost
 │     Identifier "func"; Version = CLI version; logger bridged to CLI
 │     FallbackHostTemplateConfigNames = ["dotnetcli"]
 │     BuiltIns = Edge defaults + RunnableProjects generator + func post actions
 ├── WorkloadTemplatePackageProvider : ITemplatePackageProvider
 │     yields mounts for every installed templates workload (all channels):
 │     folder mounts (Node/Python trees), nupkg mounts (DotNet);
 │     provenance map: mountPointUri → (packageId, version, stack, channel)
 ├── FuncPostActions/
 │     AppendToHostFilePostActionProcessor  (func-owned GUID; §2.5)
 │     ManualInstructionsProcessor          (standard AC1156F7…)
 ├── MsEngineProvider              : ITemplateEngineProvider (EngineId "mste")
 │     ListTemplatesAsync → cached engine query → filter by stack/language/
 │        selected workload → FunctionTemplateInfo
 │     ApplyAsync → dry-run (conflicts → AlreadyExists) → InstantiateAsync
 │        → allowed post actions → Created(files) | typed failure
 └── EngineCache/  (IPathInfo override → <workload-home>/template-engine/…)

Workloads                  src/Workloads/Templates/<stack>/  kind: content pkgs
 └── payload per §2.4
```

### 2.2 Contract evolution (revised by D20)

- **The `ITemplateEngineProvider` seam is deleted** (reconciliation fork
  4b, D20): the orchestrator calls the CLI-internal engine service
  directly. `ITemplateEngineProvider`, `ITemplateEngineProviderRegistry`,
  and `EngineIds` are removed from `src/Abstractions/Templates/`;
  engine-zone directory sniffing dies with them; the `engine id` telemetry
  axis is dropped.
- `FunctionTemplateInfo` (minus `EngineId`), `TemplateUserPrompt`, and the
  sealed `TemplateApplicationResult`/`Failure` hierarchy survive as the
  orchestrator-facing shapes, now populated live from
  `ITemplateInfo.ParameterDefinitions` (+ host-file overrides) instead of
  two bespoke readers.
- Everything stays CLI-internal; **no new public abstractions surface**.

### 2.3 Engine lifecycle in a `func new` invocation

1. Orchestrator steps 1–5 (project/profile gates, workload presence check,
   language resolution) — the former channel-match sub-step is deleted
   (D16: gating moved into template constraints; there is one templates
   package per stack, plain semver).
2. First engine touch lazily builds `EngineEnvironmentSettings` +
   `TemplatePackageManager` (kept for process lifetime).
3. Provider yields templates-workload mounts under the **yield rule**
   (simplified by D16): for each templates package id, exactly one mount —
   the **highest installed version**. Older side-by-side versions stay on
   disk (rollback) but are never mounted: mounting multiple versions would
   collide on template `identity`, and the engine's last-yielded-wins
   dedupe would make the survivor an accident of yield order. Uninstalling
   the highest version changes the yield set to the next-highest → the
   engine detects the package-list change and rebuilds lazily (D7) on the
   next `func new`. The provenance map carries
   `mountUri → (packageId, version, stack)`.
4. List → evaluate **constraints** (D16: `func-extension-bundle` against
   the project's resolved bundle, plus built-in `host`/`os`) → project to
   `FunctionTemplateInfo` (stack from provenance, language from
   `tags.language`, trigger from `azfunc-trigger` tag, prompts from
   parameter definitions + host file). Constraint-restricted templates are
   hidden from selection but retained for call-to-action rendering.
5. Steps 7–11 unchanged (picker, two-stage parse, name, bundle presence
   check — the min-bundle version gate is subsumed by constraints, D16).
6. Apply: `GetCreationEffectsAsync` dry-run → conflicts without `--force` →
   `AlreadyExists`; else `InstantiateAsync` with stage-B parameters → run
   allowlisted post actions → `Created(files)`. Status mapping:
   `MissingMandatoryParam`/`InvalidParamValues` → `InvalidPrompt`;
   `CreateFailed` → `WriteFailed`/`ProviderError`.

### 2.4 Templates workload payload (new layout) — ⚠️ SUPERSEDED BY §2A (D21)

> The `tools/any/content/…` workload-payload layouts below are **obsolete**:
> packages are now standard engine template packages (templates at the
> package root under `<TemplateName>/.template.config/`), identified by
> package type, installed by the engine (see §2A and the
> `template-packages` spec). Retained for historical context only.

**Node / Python** (authored, human-diffable):

```
tools/any/content/
  templates/
    HttpTrigger/
      .template.config/
        template.json                    ← identity, shortName ["http", legacy aliases],
        │                                  symbols, sourceName, constraints (D16),
        │                                  tags { language, azfunc-stack, azfunc-trigger }
        func.host.json                   ← func CLI hints (aliases, hidden, validators)
        localize/templatestrings.*.json  ← optional i18n
      <content files>
    …
```

No sibling manifest: per-template gating lives in each `template.json`
`constraints` block (D16 — the `templates-workload.json` sidecar is
deleted).

One filesystem mount per `templates/` root; the engine's scanner discovers
every `.template.config/template.json` beneath it.

**DotNet** (upstream artifact verbatim):

```
tools/any/content/
  packages/Microsoft.Azure.Functions.Worker.ItemTemplates.<ver>.nupkg
```

Zip-mounted directly — no extraction, no hive, no `dotnet new install`, no
`source.json`, no `dotnet-templates.json`, no sibling manifest.

Carried over unchanged: package ids, `kind: content`, `FuncCliWorkload`
packageType, `alias:<stack>-templates` tags. **Deleted by D16:** the
`minBundle:` tag, channel prerelease labels (packages use plain semver;
one package per stack ships the full template set), and the entire Node
pack-time per-channel subsetting pipeline (`_bindings.json`, `index.json`
fetch, HTTP-Range `extensions.json` extraction) — per-template
`func-extension-bundle` constraints express binding requirements against
the project's *actual* bundle instead.

### 2A. Acquisition, search, and project templates (pivot 2026-07-30; D21–D27)

> Supersedes the workload-acquisition parts of §2.1/§2.3/§2.4: the
> `WorkloadTemplatePackageProvider`, yield rule, provenance map, and
> `kind: content` payload wrapping are **deleted**. Constraints gating
> (D16), `func.host.json` (D9/D10), post actions (D6/D11/D13), and the
> engine-host fundamentals (D1) are unchanged.

**Acquisition (D21).** Template packages are standard engine packages,
managed by the engine's `TemplatePackageManager` against the func-owned
hive (`<func-home>/template-engine/func/…`). The engine's package store +
template cache are the **source of truth** for what's installed. No
workload registry involvement, no `workload.json`, no `FuncCliWorkload`
type. NuGet **package types** identify func templates:
`FuncItemTemplates` (function/item templates → `func new`) and
`FuncAppTemplates` (project templates → `func init`; D26). A single
package may declare both. Official packages (D28): .NET keeps the
upstream `Microsoft.Azure.Functions.Worker.ItemTemplates` /
`…Worker.ProjectTemplates` ids with func package types added at the
source; Node/Python each ship one new dual-type package
`Microsoft.Azure.Functions.Templates.<Stack>` (items + `Empty`). Index
URI + cadence: D29 (`aka.ms` vanity → Functions CDN; daily incremental
pipeline with on-demand trigger).

**Command surface (D26 — fold into `func new`/`func init`; no
`func templates` tree).**

| Surface | Operation |
|---|---|
| `func new --search [term] [--source <feed>]` | Search the func template index (default) or a specified feed's NuGet search API directly (`--source`; needed for local/private feeds) |
| `func new --install <pkg[::ver]> [--source <feed>]` | Engine-managed install of any template package (item or app types) |
| `func new --uninstall <pkg>` | Engine-managed uninstall |
| `func new --update [<pkg> \| --all]` | Compare installed vs online; install newer (explicit update path, D24) |
| `func new --list` / scaffold | Installed item templates only (offline truth = engine cache) |
| `func init` wizard / `--template <id>` | Project-template selection step (D23/D27) |

**Search (D22).** A func-owned discovery service — built on
`Microsoft.TemplateSearch.TemplateDiscovery` — scans configured feeds
(nuget.org first) with package-type queries (`packageType=FuncItemTemplates`,
`packageType=FuncAppTemplates`), prefilters for `template.json` presence,
scans candidates with the real engine, and publishes the reverse index in
the `NuGetTemplateSearchInfoVer2.json` search-cache format (incremental
`--diff` runs; `nonTemplatePacks.json` skip-list). The CLI consumes it via
an engine search-coordinator provider pointed at the func index URI
(local-file override supported for air-gapped/dev scenarios). `--source`
adds CLI-side direct feed queries at invocation time for feeds the service
never sees.

**Project templates + `func init` (D23/D27).** Per-stack **`Empty`**
project templates (`tags.type: "project"`) reproduce today's `func init`
output per stack. The init wizard becomes: stack selection (unchanged) →
**project-template selection** — default `Empty`, plus other installed
app templates and index results filtered to the chosen stack
(`--template <id>` bypasses the prompt). Selecting an uninstalled index
entry installs it (with a message). `func init` retains CLI-owned post
steps: it writes `.func/config.json` itself (templates never author CLI
config). `func new` remains the item-template path (D25).

**First-run acquisition (D27).** When `func init` resolves a stack whose
official template packages are not installed, it **auto-installs them**
from the configured feed with a clear message (offline at that moment →
actionable error). `func setup` MAY pre-install template packages as part
of profile-driven setup. `func new` never auto-installs — missing
templates produce the install hint (`func new --install …`).

**Offline & updates (D24).** Offline = engine cache: `func new --list`,
`--template <id> --help`, and scaffolding work fully offline against
installed packages. `func new --update` (and the background staleness
hint, reused from the workload subsystem's pattern) compares installed
versions to the online source and installs newer.

### 2.5 Python v2 append — decided shape (D13; mechanics gated by SP-2)

Python v2 model: functions register on a shared `app` object in
`function_app.py`, or on `bp = func.Blueprint()` in a blueprint file routed
via `--file`. The V2 engine used **four alternative jobs** per template with
near-duplicate contents; under the engine this collapses to **one template,
one snippet**, because the flows differ only in target file, header, and
decorator object — all parameterizable:

- Content: a single staged snippet (`__snippet__.py`) — the decorated
  function; `sourceName` carries the function name; symbols carry inputs
  plus two CLI-driven knobs: `AppObject` (hidden; `"app"` or `"bp"`) and
  `AppFile` (`--file`, default `function_app.py`).
- `template.json` declares a post action with a **func-owned actionId
  GUID**; `AppendToHostFilePostActionProcessor` (func host) resolves the
  target, writes a per-flow header when creating, appends with separator
  hygiene, deletes the staged snippet, reports the target as output.

**Flow matrix (D13):**

| Invocation | Target state | Behaviour |
|---|---|---|
| no `--file` | `function_app.py` exists | append, bound to `app` |
| no `--file` | `function_app.py` missing | **create with full app header** then append (v4 parity; forgiving of a deleted file) |
| `--file api.py` | missing | **create blueprint**: header (`import azure.functions as func` + `bp = func.Blueprint()`) + snippet bound to `bp`; **print registration instructions** (`from api import bp` / `app.register_functions(bp)`) — no auto-edit of `function_app.py` (regex-editing arbitrary user Python is riskier than the parity behaviour; auto-insert is a possible future nicety) |
| `--file api.py` | exists | append, bound to `bp` |

**Hardening rules:**

- **Duplicate guard:** scan target for `def <FunctionName>(` before
  appending; present → error, no `--force` override (appending a duplicate
  is never right).
- **Import posture (authoring guideline):** snippets assume the standard
  preamble (`import azure.functions as func`, `import logging`) and must
  not require new imports.
- **Blueprint variable assumption:** append-to-blueprint binds `bp` (what
  our create-flow wrote) — same limitation as today.
- **Dry-run compensation:** `GetCreationEffectsAsync` can't see the append;
  the provider adds the append target to its own conflict/reporting logic
  and renders it as `Modified:`.
- **Staging isolation (S7 refinement):** append-flow templates are
  instantiated into a provider-owned scratch staging directory, not the
  project; only the append processor touches the project. A failed append
  orphans nothing and can hint at the staged snippet for manual recovery.

Fallbacks if SP-2 disappoints on mechanics: CLI-side composition (engine
renders to virtualized FS, func decides write-vs-append) or a Python UX
change to blueprint-per-function (needs Python-team buy-in). Rejected:
keeping Python on the V2 engine (contradicts D2).

### 2.6 Post-action & security policy (D6, amended by D11; dispatch model confirmed §6B)

> **Dispatch model (spike-confirmed):** the engine has **no
> `IPostActionProcessor`** — post-action processors are not engine
> components. The engine returns `ICreationResult.PostActions` (each an
> `IPostAction` with `ActionId` GUID, `Args`, `ManualInstructions`,
> `ContinueOnError`); **the host runs its own code keyed by `ActionId`**.
> So the "allowlist" below is a func host-side dispatcher: a fixed switch
> on known ActionIds. Unknown ActionIds fall through to their
> `ManualInstructions`. This is strictly a host concern — templates cannot
> introduce executable behavior.

Dispatched ActionIds (allowlist):

1. **Func append processor** (§2.5).
2. **Manual-instructions display** (`AC1156F7…`).
3. **Add package/project reference** (`B17581D1…`) — *added by D11*: the
   real isolated-worker templates in `Functions.Templates` use it to add
   the required extension `PackageReference` (e.g.
   `Worker.Extensions.Http.AspNetCore`, TFM-conditional version) to the
   user's `.csproj`, with **empty manual instructions** — leaving it
   unregistered would silently scaffold broken .NET functions. Func
   implements it as a targeted csproj XML edit.

Everything else (run-script, restore, open-in-editor, …) is not dispatched
and degrades to its `manualInstructions` text (or silently skips when the
instructions are empty — e.g. the VS-oriented open-in-editor action). The
engine component set is fixed at host construction. **Spike first-pass
(§6B):** no code from a template payload was executed — scanning is pure
`template.json` parsing and templates cannot register components; a
dedicated adversarial SP-4 test (malicious component pack) remains for
implementation.

**Bind-source obligation (D11):** the real templates bind
`msbuild:TargetFramework` to select TFM-appropriate package versions; the
engine's built-in bind sources are `host:`/`env:` only (`msbuild:` comes
from the `dotnet new` CLI host). Func registers a small
`IBindSymbolSource` that answers `msbuild:` prefixed bindings by reading
the project file (e.g. `TargetFramework` from the `.csproj`); without it,
`Framework` silently falls back to its `defaultValue` and computed
package-version conditions can pick wrongly. In scope for SP-1/SP-2.

### 2.7 Authoring & conversion pipeline

- **Node:** one-time converter from the 33 in-repo V2 entries → template
  folders (inline `files` → real files; `$(FUNCTION_NAME_INPUT)` →
  `sourceName`; userPrompts → `symbols`), hand-finished per template
  (finally tokenising per-binding literals — closes the old
  templates-workload-spec §8 follow-up).
- **Python:** authored conversion of the bundle's `StaticContent/v2` Python
  entries → template folders with append post actions. Ends the pack-time
  bundle dependency; ⚠️ Python template updates then flow through this repo
  (like Node today) instead of riding bundle republishes — socialize with
  Python owners.
- **DotNet:** no authoring — bump the pinned nupkg version. Func-specific
  hints for upstream templates: OQ-10.

### 2.8 What carries over unchanged

`func new` pipeline steps 1–11/13, two-stage parse, picker, `--list` + JSON
envelope + help catalog + tab completion, channel matching, minBundleVersion
enforcement, profile/project/language gates, workload install UX, typed
failure hierarchy, telemetry shape (engine-id axis constant), offline and
no-auto-install postures.

---

## 3. Performance & state

> Numbers below updated with **Task 0 spike measurements (2026-07-30, §6B)**.

- **Cache** lives in the func hive (`<func-home>/template-engine/func/
  <cli-version>/…`, D21 relocates the engine's global settings dir there —
  see the isolation finding in §6B). The engine maintains it as part of
  install/uninstall/update; CLI-version-scoped dirs prevent cross-version
  poisoning. **Measured:** cold list ~120 ms, warm list 0 ms (1-template
  local hive); real NuGet install ~1.9 s. Concurrency/locking still to
  verify (SP-3).
- **Budgets:** engine init + cached list comfortably fit `func new`'s
  interactive-latency class on the spike's small hive; re-measure at
  realistic corpus size during implementation.
- **Single-file publish: PROVEN (SP-1).** The engine runs correctly from a
  self-contained `PublishSingleFile` exe (validated end-to-end: install +
  scan + 36-template load from the single binary). Guard with
  `VerifySingleFilePublish`; `IsAotCompatible` keeps native-AOT viable.
  **Measured engine-lib size = 0.84 MB** (Edge 294 KB, RunnableProjects
  264 KB, Core 124 KB, Utils 100 KB, Abstractions 55 KB, Core.Contracts
  21 KB) — the earlier "+2–3 MB" estimate was high; real growth ≈ ~1 MB
  engine libs + partial NuGet.* overlap (func already ships
  NuGet.Packaging/Protocol), offset by deleting two engine csprojs.
  **Caveat:** single-file moves `AppContext.BaseDirectory`; the CLI must
  resolve any source/local paths robustly (installing by package id into
  the hive — the normal path — is immune).

---

## 4. Migration / deletion inventory

| Area | Action |
|---|---|
| `src/Templates.V2/`, `src/Templates.DotNet/` | Delete entirely |
| `src/Templates.Engine/` (+ tests) | New |
| `src/Abstractions/Templates/EngineIds.cs` | Shrink to single id |
| `src/Func/Templates/` orchestrator | Remove engine-zone detection; hydrator source switches to parameter definitions; selection policy untouched |
| `src/Workloads/Templates/*` (all three projects) | **Leave the workload system** (D21): become standard template-package projects (new location, e.g. `src/Templates.Packages/<Stack>` — naming at implementation time); Node content converted to `.template.config` trees; Python authored from the bundle corpus; DotNet content retired here entirely (official packages are the upstream `Worker.*Templates` ids, D28) |
| `eng/build/Workloads.Templates.targets` + channel-filter scripts (`fetch-bundle-extensions-json.ps1`, `filter-node-templates-by-bundle.ps1`, `_bindings.json`) | Delete (D16/D21): standard template nupkg packing replaces workload packing; constraints replace pack-time subsetting |
| `src/Func/Templates/{InstalledTemplatesWorkloads, TemplatesChannelMapper, TemplatesWorkloadManifestReader}` + `IInstalledTemplatesWorkloads` | Delete (D16/D21): no workload enumeration, no channel mapping, no sidecar manifest |
| Old dotnet template hive dir | Obsolete — left on disk, release-note documentation only (D14) |
| Specs | `templates-workload-spec.md` §4.3/§5/§6 and `func-new.spec.md` §4.3/D9/Q3/Q-Eng-A revised; requirement-level deltas land in this change's `specs/` |

User-visible deltas:

- .NET scaffolding no longer needs `dotnet` on PATH at `func new` time.
- DotNet templates workload install has no provisioning step (and loses its
  partial-hive failure mode + re-install hint).
- `--template` matches `shortName`. Per D8, Node/Python display **short,
  language-free ids** (`HttpTrigger`); the legacy suffixed ids
  (`HttpTrigger-JavaScript`, …) remain accepted as hidden aliases, so
  scripts don't break. DotNet keeps upstream shortNames (`http`, …).
  Catalog display changes; acceptance doesn't (verified in SP-5).

---

## 5. Open questions & spikes

### Open questions

| # | Question | Leaning |
|---|---|---|
| OQ-8 | ~~Regex prompt validators~~ | **Resolved → D9** (`func.host.json` block) |
| OQ-10 | ~~func-specific hints for DotNet templates~~ | **Resolved → D10** (we own the upstream repo; `func.host.json` contributed at source; no `dotnetcli.host.json` exists to inherit) |
| OQ-16 | ~~Authoring home for Node/Python template.json content~~ | **Resolved → D12** (core-tools now; declared migration to `Functions.Templates` later) |
| OQ-17 | PowerShell templates — v5 CLI now has a PowerShell stack; this change covers Node/Python/DotNet, but `Functions.Templates` has PowerShell corpus | Open — scope question for a follow-up change |
| OQ-18 | ~~Command surface~~ | **Resolved → D26** (fold into `func new`/`func init`; no separate tree) |
| OQ-19 | ~~Project-template package type name~~ | **Resolved → D26** (`FuncAppTemplates`) |
| OQ-20 | ~~First-run acquisition UX~~ | **Resolved → D27** (auto-install at init; `func setup` pre-install; `func new` hint-only) |
| OQ-21 | ~~Search over local/other feeds~~ | **Resolved** (index default + CLI-side direct `--source` feed query; §2A) |
| OQ-22 | ~~Official package ids / index hosting / cadence~~ | **Resolved → D28 + D29** (.NET keeps upstream ids with func types added; Node/Python dual-type `Microsoft.Azure.Functions.Templates.<Stack>`; aka.ms → Functions CDN; daily incremental pipeline) |
| OQ-23 | ~~`func init` vs `IProjectInitializer` split~~ | **Resolved → D31** (thin metadata contract; init core orchestrates; init options become template symbols; dotnet-new-func shell-out deleted) |
| OQ-11 | ~~Cleanup of the orphaned dotnet template hive~~ | **Resolved → D14** (leave on disk; release-note documentation only) |
| OQ-12 | ~~Node/Python shortName scheme~~ | **Resolved → D8** (short ids + legacy aliases) |
| OQ-13 | ~~Does `templates-workload.json` grow fields?~~ | **Resolved → D15** (stays minimal: `minBundleVersion` only) |
| OQ-14 | ~~Python blueprint UX details~~ | **Resolved → D13** (§2.5 flow matrix: create-with-header for missing app file; blueprint registration via printed instructions, no auto-edit) |

### Spikes (gate the design; run before tasks.md)

| # | Spike | Success criteria |
|---|---|---|
| SP-1 | Host Edge+RunnableProjects in a net10 single-file console; mount folder tree + .nupkg; list + instantiate | Works under single-file publish; size delta + cold/warm timings recorded |
| SP-2 | Custom append post action (Python flow incl. `--file`) | Correct scaffold + append; dry-run/`AlreadyExists` story defined |
| SP-3 | Cache under custom `IPathInfo`; invalidation on install/update; concurrent runs | No stale catalog; no corruption |
| SP-4 | Security: mounted packages can't cause assembly load/code exec; allowlist enforced | Documented proof + tests |
| SP-5 | Convert 3 Node + 1 Python template; consume upstream DotNet nupkg; diff list/help/scaffold vs current engines | Parity, or intentional deltas listed |

**Agreed next step (proposed 2026-07-28):** a combined walking-skeleton spike
(SP-1 + SP-2 + a slice of SP-5) in a scratch harness, before drafting
`proposal.md` — it retires the only decision-threatening risk (§2.5) and
turns §3's estimates into measurements.

---

## 6. User-journey walkthrough (design review spine)

> ⚠️ **Partially superseded by the 2026-07-30 pivot (§2A, D21–D30):** the
> S1 (install) and S8 (updates) notes below describe the retired
> *workload* acquisition model. Under the pivot: acquisition = engine
> package manager via `func new --install/--update` + init auto-install
> (D26/D27); the yield rule/provenance/lazy-cache mechanics are gone.
> S3–S7 and S9 remain accurate except where they mention workload
> packaging. Discovery/search is a new journey stage not covered by the
> original walkthrough (see the `template-search` spec).

> We review the design in the chronological order a user experiences it,
> resolving each stage's open questions and spike dependencies as they
> appear. Status column tracks the review, not implementation.
> The gating spike is parked as **Task 0** in `tasks.md` and executed later.

| Stage | User moment | What changes under this design | Attached OQs / spikes | Review status |
|---|---|---|---|---|
| S1 | `func workload install <stack>-templates` | New payloads land (folder trees / embedded nupkg); **DotNet: no hive provisioning step** | OQ-15 → resolved (D7: lazy cache, no special-casing) | ✅ reviewed 2026-07-28 |
| S2 | `func init` | Nothing (out of scope) | — | ✅ n/a |
| S3 | `func new --list` / `func new --help` catalog | Catalog served from engine cache; ids = shortNames | OQ-12 → resolved (D8) | ✅ reviewed 2026-07-28 |
| S4 | `func new --template <id> --help` | Options hydrated live from parameter definitions + host files | OQ-8 → D9, OQ-10 → D10; D11 discovered here; new OQ-16/OQ-17 opened | ✅ reviewed 2026-07-28 |
| S5 | Scaffold, create-flow (Node, DotNet) | Engine dry-run + instantiate; no `dotnet` shell-out | SP-1 (deferred, Task 0) | ✅ reviewed 2026-07-28 |
| S6 | Scaffold, append-flow (Python) | Append post action (§2.5) | OQ-14 → D13; SP-2 (deferred, Task 0) | ✅ reviewed 2026-07-28 |
| S7 | Failure paths (channel, min-bundle, conflicts, missing workload) | Policy unchanged; 3 failure modes deleted; per-template scan isolation; staging-dir refinement for append | — | ✅ reviewed 2026-07-28 |
| S8 | Updates (workload update/prune; CLI upgrade) | Cache invalidation; orphaned hive cleanup | OQ-11 → D14, OQ-13 → D15; SP-3 narrowed to concurrency only | ✅ reviewed 2026-07-28 |
| S9 | Publisher journey (author + ship a template) | Authoring pipeline §2.7; conversion tooling | SP-5 (deferred, Task 0) | ✅ reviewed 2026-07-28 — **walkthrough complete** |

### Reconciliation review — `func-universal-template-engine` (2026-07-28)

Compared this change against jviau's parallel OpenSpec change
(`docs/specs/func-universal-template-engine` on
`u/jviau/vnext/templates-spec` in Azure/azure-functions-core-tools). Both
independently chose the identical engine core (in-proc Edge +
RunnableProjects, host id `func`, isolated settings, template.json-only,
groupIdentity/shortName variants). Four architectural forks were reviewed
one-by-one and ruled:

| Fork | Theirs | Ruling |
|---|---|---|
| 1. Acquisition | Engine `TemplatePackageManager` installs into func hive; install front-door routes by package type; templates leave the workload system | **Keep ours** (D17 reaffirms D5): workload acquisition is the single lifecycle; provider mounts payloads read-only |
| 2. Gating | Per-template `template.json` constraints (`func-extension-bundle` + host/os); restricted + call-to-action; channel axis dissolved | **Take theirs, adapted** (D16): constraints own gating; channel axis, sidecar manifest, `minBundle:` tag, and Node pack-time subsetting all deleted; `MissingExtensionBundle` stays a hard error under the project mandate; assume-latest-stable deferred |
| 3. Project gate | Not project-mandated; `--language` override; empty-dir scaffolding; cross-stack advisory | **Keep ours** (D18): project mandate + no `--language` stand (single-runtime apps make cross-stack scaffolds broken, empty-dir scaffolds incomplete; reverses settled func-new decisions). Their mode recorded as future product discussion with jviau |
| 4a. Ids | One shared `shortName` across all stacks | **Consolidate** (D19, amends D8): unified upstream-style ids (`http`, `timer`, …) displayed for every stack; `HttpTrigger` + legacy suffixed forms stay accepted aliases; no cross-stack groupIdentity machinery (stack pre-pinned per fork 3) |
| 4b. Provider seam | `ITemplateEngineProvider` deleted | **Take theirs** (D20): seam, registry, and `EngineIds` deleted; orchestrator calls the engine service directly |

Coverage complementarity (for the merge conversation): their change has no
Python append design, no post-action allowlist/add-reference processor, no
`msbuild:` bind source, no `func.host.json`, and no cache-mechanics
decisions — all covered here. Ours lacked their constraint gating (now
adopted) and their failure-precedence/call-to-action rendering (adopted
with it).

### S9 review notes (2026-07-28)

- Adding a template input = a symbol + a `func.host.json` line; hydrates
  automatically, no CLI release — the "no CLI release for new inputs" goal
  now holds with zero bespoke schema.
- Node `_bindings.json` channel filter carries over (build fails on
  missing entry; filter excludes template folders).
- **Authoring loop upgrade:** per-mount timestamp diffing means editing an
  installed payload in place is picked up on the next `func new` — no
  reinstall, no repack during template development.
- DotNet: author in `Functions.Templates` (incl. `func.host.json`, D10);
  workload build is PackageDownload + embed, no hydration targets.
- One-time conversion tooling (V2 → folder trees) + SP-5 parity diff;
  expected deltas limited to D8 short-id display.
- End state per D12: trees migrate to `Functions.Templates`; all stacks
  embed published nupkgs.

### S8 review notes (2026-07-28)

- Workload update/prune/uninstall: standard in-place swap; yield rule +
  cache set-diff handle refresh lazily (D7 holds; nothing templates-special
  in the installer). Channel switch via `host.json` edit is a query-time
  filter change — zero rebuild.
- CLI upgrade: version-scoped cache dir → one cold scan per CLI version;
  **design note:** after a successful cache write, opportunistically delete
  stale sibling version dirs under `template-engine/func/` (tiny metadata,
  our own tree — no decision ceremony needed).
- **OQ-11 → D14:** the obsolete pre-change dotnet hive is left on disk and
  documented in release notes only; no cleanup code (user preference:
  zero-risk over tidiness; the dir is inert and exists only on v5 preview
  machines).
- **OQ-13 → D15:** `templates-workload.json` stays minimal
  (`minBundleVersion` only). Every candidate field found a better home
  (tags, `func.host.json`, provider provenance map).
- SP-3 narrowed to **concurrency verification only**: settings/cache
  locking under two simultaneous `func new` rebuilds; update landing
  between reconcile and query. Invalidation logic already verified from
  engine source (context.md §6.7).

### S7 review notes (2026-07-28)

- All policy failures (project gate, profile, workload-missing, channel,
  min-bundle, language, unknown template, parse errors) carry over
  verbatim; unknown-template errors are *less* likely thanks to D8 legacy
  aliases.
- **Deleted failure modes:** `dotnet`-not-on-PATH; hive missing/partial
  (the DotNet variant of pipeline step 11b disappears); 5-minute
  provisioning timeout.
- **Per-template scan isolation:** a corrupt `template.json` skips that
  template with a `[packageId]`-prefixed warning instead of killing the
  stack's whole catalog (V2 behaviour); zero-template workload → reinstall
  hint. Corrupt cache self-heals (engine rebuilds on parse failure).
- **Post-action failure semantics:** upstream add-reference actions are
  `continueOnError: true` → failed csproj edit degrades to a warning with
  manual-add instructions on a successful scaffold; the func append action
  is `continueOnError: false` → typed hard failure.
- **Refinement adopted into §2.5:** append-flow templates instantiate into
  a provider-owned **scratch staging directory**; only the append
  processor touches the project. No orphaned `__snippet__.py` on failure;
  degraded-mode hint can point at the staged file.

### S6 review notes (2026-07-28)

- Walked all four Python flows (append-to-app, missing-app, blueprint
  create, blueprint append) with before/after renderings; V2's four
  alternative jobs collapse to one template + one snippet with hidden
  `AppObject`/`AppFile` symbols set CLI-side per flow.
- **OQ-14 resolved as D13**: missing `function_app.py` → create with full
  app header (parity); blueprint registration → printed instructions
  (parity; auto-insert deferred as future nicety); duplicate-function
  guard with no `--force` override; snippets must not require new imports;
  append-to-blueprint assumes `bp`.
- SP-2 (deferred, Task 0) now validates **mechanics only** — post-action
  wiring, header writing, separator hygiene — the UX shape is decided.

### S5 review notes (2026-07-28)

- Authored content trees are laid out as the **target** layout
  (`src/functions/HttpTriggerFunc.ts`); `sourceName` + `--name` rename
  files and body tokens together — replaces the V2 engine's
  `filePath` action arguments.
- `--force` maps to the engine's overwrite mode; the `AlreadyExists` gate
  runs on the dry-run's reported file operations and is **scoped to
  template outputs** — post-action effects (csproj edit, append targets)
  are not conflicts; processors are written idempotent (e.g. skip if the
  `PackageReference` already exists).
- Result rendering (and `--output json`) reports the **union** of engine
  creation results (`Created:`) and post-action processor reports
  (`Modified:` — csproj for DotNet, append target for Python).
- DotNet on net6.0 exercises D11 end-to-end: `msbuild:TargetFramework`
  bind → computed symbols pick the TFM-correct extension package version →
  add-reference processor edits the csproj (previously `dotnet new`'s job).
- No open questions; SP-1 (hosting feasibility, timings, parity diffs)
  deferred as tasks.md Task 0.

### S4 review notes (2026-07-28)

- Hydration UX unchanged; source switches to live `ITemplateInfo`
  parameter definitions + `func.host.json` (D9: aliases, regex validators,
  functionName validator — engine-inert, hydrator-read).
- **Ground-truthed against the real corpus** (`Functions.Templates` at
  `C:\root\repos\templates`, 237 template dirs, all languages):
  - No `dotnetcli.host.json` exists anywhere — the "inherit upstream
    dotnetcli hints" assumption was false; option names always came from
    raw symbol names. We own that repo → `func.host.json` is contributed
    at the source for DotNet too (D10).
  - Isolated-worker templates **depend on** the add-package-reference post
    action (`B17581D1…`, TFM-conditional extension `PackageReference`s,
    empty manual instructions) and the `msbuild:TargetFramework` bind
    symbol → D11 adds both to the func host (csproj-edit processor +
    `msbuild:` bind source), amending D6's allowlist.
  - VS-only post actions (open-in-editor) skip silently under the func
    host (unregistered, empty manual instructions) — acceptable.
  - The repo also holds the historic v4-format corpus for JS/TS/Python/
    PowerShell → opens OQ-16 (authoring home for the template.json
    conversions) and OQ-17 (PowerShell templates workload scope).

### S3 review notes (2026-07-28)

- Catalog rendering, JSON envelope, `--help` "Available templates" section,
  and tab completion carry over unchanged; rows now come from the engine
  cache (list is already narrowed by stack + channel-matched workload +
  resolved language before rendering). DotNet C#/F# dedupe via
  `groupIdentity` becomes engine-native.
- **OQ-12 resolved as D8:** Node/Python templates use **short, language-free
  ids** (`HttpTrigger`, `TimerTrigger`, …) as the displayed/canonical
  `shortName`; the project's resolved language picks the variant (same
  mechanism as C#/F# on DotNet). The legacy suffixed ids
  (`HttpTrigger-TypeScript`, `BlobTrigger-Python`, …) ship as **additional
  shortName aliases** — undisplayed, still accepted — so existing scripts
  and muscle memory keep working; deprecable in a later release. DotNet
  keeps upstream shortNames (`http`, `timer`); cross-stack id uniformity is
  explicitly not a goal (users only see their own stack's catalog).
- User-visible delta for the migration table: catalog NAME column shows
  short ids; suffixed ids disappear from display but not from acceptance.

### S1 review notes (2026-07-28)

- Install UX is unchanged on the surface (`func workload install
  node-templates` etc.); the DotNet workload loses its provisioning step —
  faster install, no network beyond the package, and the partial-hive
  failure mode + its re-install hint disappear entirely.
- **OQ-15 — resolved as D7:** the engine cache is **lazy** — the first
  `func new` after an install/update/prune pays the cold scan; `func
  workload install` stays generic with **no templates special-casing**.
  Warm-at-install was rejected: it would require the install pipeline to
  recognize templates workloads and invoke the engine — the same layering
  violation as the DotNet hive provisioning this design deletes (the CLI's
  responsibility for `content` packages ends at install/registry). Escape
  hatch: if SP-1/SP-3 measure an egregious cold scan (>~500 ms), revisit
  via a *generic* content-changed notification seam any consumer could
  subscribe to — never a hard-coded templates branch in the installer.

## 6B. Task 0 spike results (SP-1 + SP-2 + SP-6) — 2026-07-30

Ran a scratch harness (net10, `Microsoft.TemplateEngine.{Abstractions,Edge,
Orchestrator.RunnableProjects,Utils}` @ 10.0.302). Full log:
scratchpad `spike/RESULTS.md`. **Verdict: GO — all load-bearing
assumptions validated, 19/19 core checks pass.**

- **SP-6 (acquisition):** hosting Edge + RunnableProjects in-proc and
  installing template packages into a **relocated func hive** works
  (~190 ms local folder, ~1.9 s real NuGet). Cold list ~120 ms / warm
  0 ms; uninstall removes visibility. **Isolation finding:** isolating
  from `dotnet new` requires overriding the engine's **global settings
  dir** (`DefaultPathInfo.globalSettingsDir`), not just the host id /
  `Bootstrapper.hostSettingsLocation` (which leaves the global path
  shared). With that override, `~/.templateengine` was provably untouched
  (mtime identical). Drives D21's hive location.
- **SP-2 (append):** all Python flows pass — append-to-existing-app
  (`app`), create-with-header when `function_app.py` missing, `--file`
  blueprint create (`bp` + registration hint, `function_app.py`
  untouched), duplicate-name rejection; staging isolation confirmed.
  **Dispatch model confirmed:** post-actions are host-side (no engine
  `IPostActionProcessor`) — see §2.6.
- **D28 (.NET path):** real upstream `Worker.ItemTemplates` from nuget.org
  loaded **36 Azure Function templates** verbatim; `http` =
  `Azure.Function.CSharp.Isolated.HttpTrigger.3.x` with params `namespace`,
  `AccessRights` — confirms the `func.host.json` aliasing need (D9).
- **SP-1 (single-file):** engine runs correctly from a self-contained
  `PublishSingleFile` exe; engine libs = **0.84 MB** (see §3). Harness
  caveat: single-file moves `AppContext.BaseDirectory`.
- **SP-4 (first pass):** no template-payload code executed (data-only
  scan); adversarial test deferred to implementation.
- **Not exercised:** update-from-feed, concurrent-install locking (SP-3),
  discovery service (SP-7), project-template/init flow (SP-8), constraint
  evaluation.

## 7. Decision log

| # | Date | Decision | Rationale | Supersedes |
|---|---|---|---|---|
| D1 | 2026-07-28 | Engine is CLI-internal (`Templates.Engine` csproj; no engine workload) | AOT viability, no new public contract, fastest startup; engine revs ride CLI releases, templates rev independently | Exploration's "engine as `kind: workload`" option; refines func-new D9/Q3 (two engine csprojs → one) |
| D2 | 2026-07-28 | All stacks move to the MS engine in one design; V2 + shell-out deleted; Python append solved up front | One engine/one format is the point; Node port mechanical; Python risk → SP-2 | templates-workload-spec §4.3 engine table; func-new §4.3 engine split |
| D3 | 2026-07-28 | Payload per-stack mix: DotNet embeds pinned upstream nupkg (zip-mounted); Node/Python authored folder trees | Zero re-packaging + provenance for DotNet; diffable content for Node/Python | `dotnet-templates.json` + `source.json` + hive; V2 inline-files payload |
| D4 | 2026-07-28 | Doc lives in this OpenSpec change | propose→apply→archive flow | Initial `docs/proposed/` placement |
| D5 | 2026-07-28 | Engine's own package management unused; non-managed provider over installed workloads; cache under workload home | `func workload` is the single package manager; offline + deterministic | — |
| D6 | 2026-07-28 | Post-action allowlist (append + manual instructions); no run-script/restore; no assembly scanning from packages | Content workloads must never execute code | — |
| D7 | 2026-07-28 | Engine template cache is **lazy** (first `func new` after a workload change pays the scan); `func workload install` has **no templates special-casing** | Preserves the "install ends at registry" layering the design just reclaimed by deleting hive provisioning; cold-scan cost expected small (verify SP-1/SP-3; >~500 ms triggers a *generic* content-changed seam, not an installer branch) | OQ-15 |
| D8 | 2026-07-28 | Node/Python template ids are **short and language-free** (`HttpTrigger`); resolved language picks the variant via `groupIdentity`; legacy suffixed ids remain as hidden `shortName` aliases; DotNet keeps upstream shortNames | Removes redundancy (language is already project-pinned before listing); mirrors the engine-native C#/F# mechanism; aliases preserve script/muscle-memory compat | OQ-12; the `--template` id parity row in §4 |
| D9 | 2026-07-28 | **`func.host.json` block** carries per-parameter CLI hints: option aliases (`longName`), regex validators (`expression` + `errorText`), and a template-level `functionName` validator. Engine-inert; read by `TemplateOptionHydrator`. | Preserves scaffold-time validation (V2 parity: today only functionName has a validator — richer rules become optional authoring improvements); zero engine changes; func owns the schema | OQ-8 |
| D10 | 2026-07-28 | DotNet templates get func hints **at the source**: we own `Functions.Templates` (`C:\root\repos\templates`), so `func.host.json` is added directly to the upstream templates (no override table, no dotnetcli fallback reliance — **no `dotnetcli.host.json` exists in that repo**; option names have always come from raw symbol names) | One host-file convention across all stacks; verified against the real corpus | OQ-10; the §6.2/§8 "inherit dotnetcli.host.json for free" assumption (falsified) |
| D11 | 2026-07-28 | Allowlist grows: func implements the **add package/project reference post action** (`B17581D1…`, targeted csproj XML edit) and a **`msbuild:` bind-symbol source** (reads e.g. `TargetFramework` from the project file) | Verified against real templates: isolated-worker templates depend on both to scaffold correctly (TFM-conditional extension `PackageReference`s with empty manual instructions); without them scaffolds are silently broken | Amends D6 |
| D12 | 2026-07-28 | Node/Python template.json content is authored **in core-tools now**, with a **declared end state** of migrating the finished trees to `Functions.Templates` (which then publishes per-stack nupkgs the workloads embed verbatim — the DotNet model for every stack) | Fast iteration in-repo while SP-2/SP-5 churn the content; migration later is mechanical folder moves; avoids building a cross-repo publishing pipeline before anything works end to end | OQ-16; refines §2.7 |
| D13 | 2026-07-28 | Python append flows: **one template + one snippet** with hidden `AppObject`/`AppFile` knobs; missing `function_app.py` → **create with full app header** (v4 parity); new blueprint → **print registration instructions**, no auto-edit of `function_app.py`; duplicate function name in target → error (no `--force`) | Collapses V2's four near-duplicate jobs; forgiving of deleted app file; avoids regex-editing arbitrary user Python; SP-2 validates mechanics only, not shape | OQ-14; sharpens §2.5 |
| D14 | 2026-07-28 | The obsolete pre-change dotnet template hive is **left on disk**; release-note documentation only, no cleanup code | Zero-risk preference; the dir is inert, CLI-owned, and exists only on v5 preview machines | OQ-11 |
| D15 | 2026-07-28 | ~~`templates-workload.json` sibling manifest stays minimal~~ **Superseded by D16** (manifest deleted entirely) | — | OQ-13 |
| D16 | 2026-07-28 | **Gating moves into per-template `template.json` constraints** (custom `func-extension-bundle` `{id, version-range}` + built-in `host`/`os`), evaluated against the project's resolved bundle; unmet → template **hidden/restricted** with ranked call-to-action. **Deleted:** channel axis (prerelease-label mapping, per-channel packages, channel-match step, `NoTemplatesWorkloadForChannel`), sidecar manifest, `minBundle:` tag, Node pack-time subsetting pipeline. **Kept:** `MissingExtensionBundle` hard error while the project mandate stands; assume-latest-stable deferred until project-less scaffolding exists | Adopted from `func-universal-template-engine` (fork 2): right granularity (per-template), right owner (the template), large machinery deletion, better UX than hard errors after selection | D15; the min-bundle/channel carry-over stance in D2-era §2.4/§2.8; `MinBundleVersionTooOld` failure |
| D17 | 2026-07-28 | **Acquisition stays workload-based** — engine `TemplatePackageManager`/install front-door rejected | One lifecycle (atomic, SxS+rollback, prune, aliases, `--source`, one registry); their model needs the workload installer anyway; D12 end state preserves standard-package reusability | Reaffirms D5 (fork 1) |
| D18 | 2026-07-28 | **Project mandate and no-`--language` stand** — their empty-dir/cross-stack-advisory mode rejected for this change, recorded as future product discussion | Functions apps are single-runtime (cross-stack scaffolds don't run); empty-dir scaffolds are incomplete; reverses settled func-new decisions; fork-2 adaptation leans on always-resolvable bundle | Fork 3 |
| D19 | 2026-07-28 | **Unified template ids across stacks**: displayed/canonical shortNames are the upstream style (`http`, `timer`, `queue`, …) for every stack; `HttpTrigger`-style and legacy suffixed forms remain accepted aliases; no cross-stack groupIdentity semantics | Stack-independent docs/muscle memory (their goal) via the alias mechanism (our machinery); DotNet needs zero changes | Amends D8 (displayed casing) (fork 4a) |
| D20 | 2026-07-28 | **`ITemplateEngineProvider` seam deleted** — interface, registry, and `EngineIds` removed; orchestrator calls the CLI-internal engine service directly; `engine id` telemetry axis dropped | With one engine and no plausible second, the seam is dead abstraction; user ruled for the cleaner end state over churn-avoidance | The §2.2 keep-the-seam stance (fork 4b) |
| D21 | 2026-07-30 | **PIVOT: templates are no longer workloads.** Template packages are consumed directly by the engine: acquisition (install/uninstall/update) runs through the engine's `TemplatePackageManager` against the func-owned hive; the **engine's template cache is the source of truth** for what's installed. No workload registry rows, no `kind: content` wrapping, no `workload.json`, no `FuncCliWorkload` package type for templates. New NuGet package types identify them (item templates: `FuncItemTemplates`; project templates: name pending OQ-19). The workload-backed provider, yield rule, and provenance map are deleted; D7's lazy-cache rule becomes moot (the engine updates its cache as part of install) | User decision 2026-07-30; aligns with `func-universal-template-engine` fork-1 position after all | D17, D5, the §2.1/§2.3 provider+yield machinery, parts of D3 (payload wrapping) and D7 |
| D22 | 2026-07-30 | **Template search is part of the design**: a func-owned discovery service (based on `Microsoft.TemplateSearch.TemplateDiscovery`) scans NuGet feeds (nuget.org, local, other sources) for func template package types and publishes a reverse-index search cache (`NuGetTemplateSearchInfoVer2.json` format); the CLI consumes it via the engine's search coordinator (`Microsoft.TemplateSearch.Common`-style provider pointed at the func index URI, with local-file override) | User decision 2026-07-30; reuses the proven dotnet new search pipeline end to end | The "no template search" implicit scope of v1 |
| D23 | 2026-07-30 | **Project templates join the design**: per-stack `type: project` templates, starting with an `Empty` template per stack that reproduces today's `func init` output; `func init` gains a project-template selection step (filtered to the chosen/parameter-supplied stack) sourced from installed templates and the search index; additional project templates are discoverable via the project-template package type | User decision 2026-07-30 | `func init` scope exclusion in this change |
| D24 | 2026-07-30 | **Offline & update posture**: offline mode lists installed templates only (engine cache); updates compare installed versions against the online source and install newer; an explicit template update/upgrade command exists | User decision 2026-07-30 | The strict offline-only posture inherited from the workload model |
| D25 | 2026-07-30 | **`func new` remains the item-template path**; project templates surface through `func init` only | User decision 2026-07-30 | — |
| D26 | 2026-07-30 | **Command surface folds into `func new`/`func init`** — no `func templates` tree: `func new --search/--install/--uninstall/--update` (any template package); `func init --template` + wizard step for project templates. Project-template package type is **`FuncAppTemplates`** (paired with `FuncItemTemplates`) | User rulings on OQ-18/OQ-19; "app" is the Functions-native word for a project | OQ-18, OQ-19 |
| D27 | 2026-07-30 | **First-run**: `func init` auto-installs the resolved stack's official template packages when missing (clear message; offline → actionable error); `func setup` may pre-install templates via profiles; `func new` never auto-installs (hint-only) | User ruling on OQ-20 (incl. func setup addition) | OQ-20; the workload-era hint-only posture at init |
| D28 | 2026-07-30 | **Official package ids**: .NET keeps upstream ids — `Microsoft.Azure.Functions.Worker.ItemTemplates` gains the `FuncItemTemplates` package type and `…Worker.ProjectTemplates` gains `FuncAppTemplates` (standard `Template` type retained alongside); Node/Python (later PowerShell) each get **one new dual-type package** `Microsoft.Azure.Functions.Templates.<Stack>` carrying item templates + the `Empty` project template | Discovery is by package type so id uniformity is cosmetic; zero .NET republish churn; upstream `Worker.ProjectTemplates` already exists in engine format (verified in `Functions.Templates` nuspecs); single install per stack for Node/Python | OQ-22a |
| D29 | 2026-07-30 | **Index hosting & cadence**: stable `aka.ms` vanity URI (e.g. `aka.ms/func/templates-search/v2`) redirecting to Functions CDN blob storage (`cdn.functions.azure.com`); daily incremental discovery pipeline (`--diff` + skip-list) owned by Core Tools, with an on-demand trigger for official releases | CDN already hosts bundles + the profile registry; vanity indirection allows backend moves without CLI releases; CLI keeps config + local-file overrides | OQ-22b |
| D30 | 2026-07-30 | **Sanity-check completion rules**: (a) `func new`'s lifecycle/search modes (`--search`/`--install`/`--uninstall`/`--update`) bypass the project and profile gates — only list/scaffold modes are project-gated; (b) on **`func init` surfaces only**, bundle constraints evaluate against the latest available bundle (resolved offline from the installed bundles workload where possible) since no project exists yet; `func new` keeps the hard-error posture; official `Empty` templates carry no bundle constraints; (c) init's wizard index lookup degrades silently to installed-only when offline | E2E audit 2026-07-30: search-before-init must work; init-time constraint evaluation had no defined context (narrow return of the deferred assume-latest rule, init-scope only) | Completes D18/D16 interplay and D23 |
| D32 | 2026-07-30 | **Task 0 spike passed — GO** (§6B). Confirms feasibility of D1/D21 (in-proc hosting + engine-managed acquisition into a relocated func hive), D13 (Python append via host-side post-action dispatch + staging isolation), D28 (real upstream `Worker.ItemTemplates` loads verbatim, 36 templates), and single-file publish (SP-1). Refinements folded in: (a) **isolation requires overriding the engine global settings dir**, not just host id; (b) **post-actions are host-dispatched by ActionId — there is no engine `IPostActionProcessor`** (§2.6 reworded); (c) **engine libs = 0.84 MB**, correcting the "+2–3 MB" estimate (§3) | De-risking spike, agreed gate before implementation | The "+2–3 MB" estimate; "register processors" wording in §2.6 |
| D31 | 2026-07-30 | **`IProjectInitializer` shrinks to a thin stack-metadata contract**: `InitializeAsync` and `GetInitOptions` are removed; the contract keeps `Stack`, `WorkerRuntimeAliases`, `DisplayName`, `SupportedLanguages`/aliases, the Q9 default function-name validator, and gains the stack's **official template package ids** (drives D27 auto-install + missing-template hints). Init core orchestrates (stack from installed stack workloads → language → ensure packages → template step → engine scaffold → CLI-owned config write); per-stack init options become `Empty`-template **symbols** hydrated by the same `TemplateOptionHydrator` as `func new`; stack-specific post-scaffold behavior uses allowlisted post actions; the DotNet `dotnet new func` shell-out (+ its hive path provider) is deleted; a stack workload without an official `Empty` template package yields an actionable init error. Stack workload remains required at init | OQ-23 ruling: one options mechanism across init/new; no code hook has a current customer; metadata home stays natural; second shell-out eliminated | OQ-23; `IProjectInitializer.InitializeAsync`/`GetInitOptions`; `DotNetProjectInitializer` scaffolding |
| D33 | 2026-07-30 | **Implementation-time placement**: converted template-package content projects live at `src/Templates/<Stack>/` (`Templates.<Stack>.csproj` → `Azure.Functions.Cli.Templates.<Stack>`), beside `src/Templates.Engine`; the func discovery service lives in this repo at `tools/TemplateDiscovery/` with an `eng/ci` pipeline publishing the index | User rulings taken while expanding tasks.md §3: template content sits next to the engine that consumes it and follows the repo folder/assembly-name convention; keeping discovery in-repo lets one change move package types, corpus, and index together and reuses the existing `eng/ci` publish patterns | design §4's "naming at implementation time"; D29 (hosting URI + cadence) unchanged |
