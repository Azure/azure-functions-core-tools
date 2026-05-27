# Templates Workload Spec

## 1. Goals

- Deliver function-scaffold templates as `kind: content` workloads,
  with **one workload per language stack** (Node, Python, DotNet).
  Each ships independently, side-by-side.
- Preserve a single, **uniform `func new` UX across stacks** even
  though the per-stack template formats and template sources differ.
  A user types `func new` the same way regardless of which stack
  workload supplied the template.
- Make template metadata drive `func new` option hydration. Adding a
  new template-specific input (e.g. `--auth-level`, `--queue-name`)
  should require **no CLI release** — just a new template metadata
  entry in the workload payload.
- Keep the CLI binary and any *runtime* workload assemblies **free**
  of templates. Templates ship only as installed content-workload
  payloads. The CLI has no built-in templates.
- Make `func new` resolution deterministic and fully offline at
  invocation time. Every template the CLI considers comes from a
  content workload already on disk. No CDN call from `func new` itself.
- Decouple template revisions from CLI revisions: a templates workload
  can be republished and reinstalled without a `func` upgrade.

## 2. Non-Goals (v1)

- **`func new` command implementation.** The orchestrator, prompt
  flow, option hydration, template-engine wiring, and exit codes
  all live in the CLI core. This spec only specifies what the
  workload contributes to that flow.
- **CDN access from `func new`.** Mirrors the bundles posture: the
  CLI performs no network I/O for templates at invocation time. Template payloads are obtained at workload
  **build time** (Node/Python) or via NuGet at `func new` time
  through `dotnet new` (DotNet) — see §6.
- **Auto-install of a templates workload on `func new`.** Missing or
  unsatisfying installed templates causes `func new` to print an
  install hint and exit non-zero (mirrors host/bundles workloads).
- **Cross-stack templates.** Each templates workload owns exactly
  one stack. A template that scaffolds artifacts spanning two
  stacks (e.g. a Node trigger + Python helper) is out of scope.
- **Contribution points / DI services.** A templates workload is
  `kind: content`; it registers no services, contributes no commands
  or interfaces, ships no entry-point assembly. All resolution is a
  CLI-core concern.

## 3. Definitions

Listed alphabetically.

| Term | Meaning |
|------|---------|
| **Channel** | One of `stable` / `preview` / `experimental`. Determines the templates workload pkg version's prerelease label. See §4.4.2. |
| **`dotnet-templates.json`** | The DotNet templates workload's lean index file under `content/`. Lists template ids that the CLI core dispatches to `dotnet new` at scaffold time. Distinct from the bundle-derived `templates.json` schema used by Node and Python (§5.3). |
| **Min bundle version** | The lowest extension-bundle version a Node/Python templates workload is known to be compatible with, expressed as a NuGet version range. |
| **Source bundle** | The extension-bundle release (id + version) whose `StaticContent/v1` and `StaticContent/v2` sub-trees are the upstream source for a Node/Python templates workload's payload. Recorded as build-time provenance; the templates pkg version is independent of the source bundle version (§4.4.1, §4.4.2). |
| **Stack** | The CLI's per-language axis: `node`, `python`, `dotnet`. |
| **Template** | A single scaffoldable unit, identified by a stack-unique `id` and `name` (e.g. `HttpTrigger-JavaScript`). Produces one or more files. The exact production mechanism is engine-specific — see §4.4, §5.4. |
| **Template metadata** | The per-template descriptor (id, language, trigger type, default function name, category, referenced user prompts, …) the CLI core reads to drive `func new`. |
| **Template payload** | The set of file contents a template scaffolds. For Node/Python, **inlined** into the `files` map of each template entry inside `templates.json` — there are no separate per-template directories on disk. For DotNet, fetched at `func new` time via the upstream NuGet template pin (§6.3). |
| **Templates workload** | A `kind: content` package whose payload is the function-scaffold templates for one language stack. One per stack: Node, Python, DotNet. |
| **Templates workload install dir** | The directory the workload subsystem extracts an installed templates workload into: `<workload-home>/workloads/azure.functions.cli.workloads.templates.<stack>/<workload-pkg-version>/`. (Path is lowercased per NuGet install-path convention; the package id elsewhere uses PascalCase.) |
| **Workload registry** | The CLI's on-disk record of installed workload packages at `<workload-home>/workloads.json`. The CLI core enumerates templates-workload rows here to find installed templates content. Read at `func new` time. |

## 4. Architecture

### 4.1 One content workload per stack

There are three workload package ids in v1:

```
Azure.Functions.Cli.Workloads.Templates.Node
Azure.Functions.Cli.Workloads.Templates.Python
Azure.Functions.Cli.Workloads.Templates.DotNet
```

Each is `kind: content`:

- No entry-point assembly; the workload subsystem records the
  registry row but does not load any code.
- The CLI core resolves the install dir by package id (mirrors
  `Azure.Functions.Cli.Workloads.Host.<rid>` and
  `Azure.Functions.Cli.Workloads.Workers.<stack>`).

> Why per-stack packages instead of one mega-package: independent
> ship cadence, smaller payloads, side-by-side install of multiple
> stack versions, and natural alignment with the existing per-stack
> packaging convention.

### 4.2 CLI core ownership

Templates workloads are content-only. **All** of the following live
in the `func` CLI core:

- Enumerating installed templates-workload rows from the registry.
- Reading the templates index under each install dir.
- Filtering by current stack or explicit `--stack`.
- Selecting one template (by `--template` or interactive prompt).
- Hydrating template-declared inputs into command-line options
  before parse.
- Invoking the right per-stack templating engine.
- User-facing output and diagnostics.

The templates workload contains **only** the template payload plus
the build-time plumbing that fetched it. It has no runtime code.

### 4.3 Per-stack divergence (details in §5–§6)

| Concern | Node | Python | DotNet |
|---|---|---|---|
| Workload payload format | `v1/{bindings,resources,templates}/` | `v1/` + `v2/{bindings,resources,templates}/` | `dotnet-templates.json` + `source.json` (NuGet pin) |
| Template source at workload build time | Extension-bundle CDN | Extension-bundle CDN | `Microsoft.Azure.Functions.Worker.ItemTemplates` NuGet pkg (pinned, not fetched at build) |
| Template files inside `.nupkg`? | Yes (inline in `files` map) | Yes (inline in `files` map) | No (resolved at `func new` time via NuGet → `dotnet new` template hive) |
| Channel axis (stable/preview/experimental)? | Yes (matches bundle channel id) | Yes (matches bundle channel id) | No (stable only; upstream NuGet preview signals propagate via `source.json`) |
| Templating engine in CLI | Node schema engine | Python schema engine (v1 + v2) | `dotnet new <id>` shell-out |

### 4.4 Versioning, channels, and bundle compatibility

Templates workloads version independently of the CLI; each per-stack
workload ships its own SemVer-tagged `.nupkg`. Node and Python
workloads additionally track an extension-bundle channel; DotNet does
not (no bundle dependency).

#### 4.4.1 Pkg version

Each templates workload has its own 3-part SemVer, decoupled from:

- the CLI version (templates republish without `func` release),
- the stack workload version (`Workloads.Stacks.<Stack>`),
- the worker workload version (`Workloads.Workers.<Stack>`),
- the Bundles workload version.

**The templates pkg version is fully independent of any bundle
version, regardless of stack.** The only version-axis relationship
between a templates workload and the extension bundle is the
min-bundle constraint (§4.4.4).

Driven by two MSBuild props in `Directory.Version.props`:

| Prop | Purpose |
|---|---|
| `$(TemplatesVersion)` | 3-part SemVer (e.g. `1.0.0`); drives `<VersionPrefix>` |
| `$(TemplatesChannel)` | `stable` (default) / `preview` / `experimental`; selects prerelease label |

| Channel | Workload pkg version (example) |
|---|---|
| stable | `1.0.0` |
| preview | `1.0.0-preview` |
| experimental | `1.0.0-experimental` |

Side-by-side coexistence via `func workload install --force` matches
the host/bundles model. Channel semantics differ by stack — see
§4.4.2 (Node/Python) and §4.4.3 (DotNet).

#### 4.4.2 Channel scheme and bundle compatibility — Node and Python

A Node/Python templates workload is built from one extension-bundle
channel's `v1/`+`v2/` content (§6.1, §6.2). The mapping is recorded
on the workload pkg version via its prerelease label:

| Templates channel (prerelease label) | Source bundle id |
|---|---|
| stable (no label) | `Microsoft.Azure.Functions.ExtensionBundle` |
| preview (`-preview`) | `Microsoft.Azure.Functions.ExtensionBundle.Preview` |
| experimental (`-experimental`) | `Microsoft.Azure.Functions.ExtensionBundle.Experimental` |

**Channel is automatically derived from the project's bundle.** At
`func new` time, the CLI reads the project's `host.json`
`extensionBundle.id`, maps it to the corresponding templates channel
via the table above, and picks the **highest installed templates
pkg whose prerelease label matches** that channel. Selecting an
experimental bundle in the project automatically resolves to the
latest installed templates in the experimental channel. There is no
explicit templates-channel selector — channel selection is fully
implicit.

If no installed templates workload matches the project's channel,
the CLI surfaces a hint pointing the user at `func workload install`
with the right channel's pkg version. There is no cross-channel
fallback (e.g. stable templates do not satisfy an experimental
project).

**Install is independent of project state.** A user may install any
channel's templates workload at any time, regardless of which (if
any) bundle the current project references. Install is governed by
explicit version (`func workload install <pkg> --version <ver>`); the
channel is implicit in the pkg version's prerelease label.

**Min-bundle compatibility check via hints.** Within the channel-
matched set, the CLI also compares the selected templates workload's
min-bundle (§4.4.4) against the project's resolved bundle version.
If the bundle is too old, the CLI surfaces a warning or error.

**Pkg version is independent of bundle version.** A Node/Python
templates workload version does **not** encode the bundle version it
was snapshot from. `$(TemplatesVersion)` follows its own SemVer
cadence; the source-bundle version is recorded internally
(workload-build provenance, not user-facing).

#### 4.4.3 DotNet templates workload versioning

The DotNet templates workload has no extension-bundle dependency
(bindings come from NuGet references in the user's `.csproj`). It
therefore has no channel mapping like Node/Python (§4.4.2):

- `$(TemplatesChannel)` defaults to **stable** in v1. No preview /
  experimental DotNet templates workload is planned for v1; future
  channels can be added if a use case appears.
- `$(TemplatesVersion)` follows its own SemVer cadence
  (e.g. `1.0.0`, `1.1.0`), independent of the upstream
  `Microsoft.Azure.Functions.Worker.ItemTemplates` NuGet version.
- The pinned NuGet template-package version lives in
  `content/source.json` (§5.3) — that's the version the CLI
  provisions into the dotnet template hive at `func new` time.

#### 4.4.4 Min-bundle version dependency

A Node/Python templates workload declares a **minimum compatible
bundle version** so the CLI can flag incompatibilities at `func new`
time when the project's resolved bundle version is too old.

- **Applies to:** Node and Python templates workloads. DotNet does
  not declare min-bundle (no extension-bundle dependency).
- **Granularity:** workload-wide for v1. Per-template granularity is
  a possible future extension to the sibling manifest below.
- **Format:** NuGet version range string (e.g. `[4.18.0, )`).
- **Enforcement:** at `func new` time only. The CLI checks the
  installed templates workload's min-bundle against the project's
  resolved bundle version and surfaces mismatches as a warning or
  error.

**Storage** is duplicated across three locations, each serving a
different consumer:

- **`content/templates-workload.json` — sibling manifest** (CLI-owned
  schema). Canonical source the CLI core reads at `func new` time.
  Carries the structured `minBundleVersion` range and (in a future
  extension) any per-template overrides. See §5.2 for placement.
- **Nuspec `<tags>` field** — `minBundle:<range>` tag
  (e.g. `minBundle:[4.18.0, )`). Workload-wide only. Lets
  `func workload list` / `search` surface compatibility without
  extracting the payload. Parsed at install time by the same
  tag-prefix parser that already extracts `alias:*` tags (requires a
  small installer extension; see §5.1).
- **`workload.json` `description` field** — a human-readable sentence
  in the manifest description, e.g. *"Python templates compatible
  with `Microsoft.Azure.Functions.ExtensionBundle` (min version
  4.0.0)."* Documentary only; surfaces in `func workload list` output.

The sibling manifest is **authoritative** for CLI compatibility
checks. The tag and description are derived and exist purely for
discoverability — they MUST agree with the manifest at build time.

## 5. Workload / Layout

### 5.1 Package identity and metadata

Each templates workload:

- **Package id:** `Azure.Functions.Cli.Workloads.Templates.<Stack>`
  (e.g. `Azure.Functions.Cli.Workloads.Templates.Node`).
- **Package type:** `FuncCliWorkload`.
- **Tags:** `kind:content alias:templates-<stack> alias:<stack>-templates func-workload`.
  Node and Python workloads additionally carry a min-bundle tag,
  e.g. `minBundle:[4.18.0, )` (§4.4.4).
- **No `<dependencies>`** in the nuspec. Templates workloads are
  self-contained; the min-bundle dependency is not expressed as a
  NuGet dependency (§4.4.4).

The `workload.json` at the package root carries the manifest plus a
compatibility hint in the description (Node/Python):

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "content",
  "displayName": "Python templates",
  "description": "Function-scaffold templates for Python Azure Functions projects. Compatible with Microsoft.Azure.Functions.ExtensionBundle (min version 4.0.0)."
}
```

`displayName` and `description` surface in `func workload list`.
The compatibility sentence in `description` is documentary; the
authoritative min-bundle value lives in `content/templates-workload.json`
(§4.4.4).

### 5.2 On-disk layout — Node and Python

Both stacks ship template content under `tools/any/content/` with
`v1/` and `v2/` directories at the root, matching the programming-model
split the Functions extension bundle uses internally. The CLI core
reads from these directories directly.

```
Azure.Functions.Cli.Workloads.Templates.Node.<version>.nupkg
├── workload.json                            (kind: content)
├── README.md
├── release_notes.md
└── tools/
    └── any/
        └── content/
            ├── templates-workload.json      ← CLI-owned sibling manifest:
            │                                  { "minBundleVersion": "[4.0.0, )" }
            │                                  (Node/Python only; §4.4.4)
            ├── v1/                          ← legacy programming model
            │   ├── bindings/
            │   │   └── bindings.json
            │   ├── resources/
            │   │   ├── Resources.json        ← en-US default
            │   │   ├── Resources.de-DE.json
            │   │   ├── Resources.fr-FR.json
            │   │   └── …                     ← i18n siblings
            │   └── templates/
            │       └── templates.json        ← Template[] (files inline)
            └── v2/                          ← v2 programming model
                ├── bindings/
                │   └── userPrompts.json     ← UserPrompt[]
                ├── resources/
                │   └── Resources*.json
                └── templates/
                    └── templates.json        ← NewTemplate[] (jobs/actions)
```

**Per-stack content of these files:**

- The **Node** workload ships `v1/templates/templates.json` populated
  with Node-language `Template` entries (`language: "JavaScript"`,
  `"TypeScript"`); `v1/bindings/` and `v1/resources/` are language-
  agnostic and copied from the bundle source. `v2/` is absent
  (Node has no v2 programming model).
- The **Python** workload ships both `v1/` (legacy `Template` entries
  with `language: "Python"`) and `v2/` (`NewTemplate` jobs/actions
  for the v2 programming model).

**Note on file representation.** Templates in both `v1/templates/templates.json`
and `v2/templates/templates.json` carry their per-template files **inline**
as a `files: { "<filename>": "<contents>" }` map within each template
object. There is no separate on-disk file tree under `templates/`. The
CLI core materialises individual files at `func new` time by reading
these inline strings.

**Mapping to the bundle source.** This layout is a filtered, repacked
mirror of the bundle's `StaticContent/v1` and `StaticContent/v2`
sub-trees. The bundle is the upstream source; the templates workload
strips the bundle's `StaticContent/` wrapper since the workload
package's `tools/any/content/` already plays that role. The bundle
channel and the templates workload channel correspond 1:1 (§4.4.2).

### 5.3 On-disk layout — DotNet

Templates files are **not** in the workload payload, and the DotNet
workload does **not** use the bundle StaticContent layout (DotNet has
no extension-bundle dependency — §4.4.3). Instead the workload ships a
small DotNet-specific index plus a pin on the upstream NuGet template
package:

```
Azure.Functions.Cli.Workloads.Templates.DotNet.<version>.nupkg
├── workload.json                            (kind: content)
├── README.md
├── release_notes.md
└── tools/
    └── any/
        └── content/
            ├── dotnet-templates.json        ← DotNet-only index (lean):
            │                                  { "templates": [
            │                                      { "id": "http", "language": "C#", ... },
            │                                      { "id": "blob", "language": "C#", ... }
            │                                    ] }
            └── source.json                  ← NuGet pin (§6.3):
                                               { "kind": "nuget",
                                                 "packageId":
                                                   "Microsoft.Azure.Functions.Worker.ItemTemplates",
                                                 "version": "<pinned>" }
```

> Naming note: the DotNet workload's index file is named
> `dotnet-templates.json` (not `templates.json`) to avoid clash with
> the Node/Python bundle schema. The two are distinct file formats
> with distinct readers in the CLI core.

When `func new --stack dotnet` runs, the CLI core reads `source.json`,
ensures the pinned NuGet template package is installed into the dotnet
template hive maintained by the CLI core, and dispatches each
`dotnet-templates.json` entry to `dotnet new <id>`.

The full `dotnet-templates.json` schema (fields per entry, optional
metadata) is open — see §8.

### 5.4 Templates and binding schemas

The templates workload uses a **CLI-owned schema** derived from the
Functions extension bundle's `StaticContent/v1/` and `StaticContent/v2/`
schemas. Minor modifications from the bundle baseline are expected
(e.g. fields the bundle doesn't have, fields the CLI doesn't consume
that may be omitted). Two top-level shapes apply, one per
programming-model version.

> Paths in the schema examples below are **bundle-source** paths.
> Workload-side paths drop the `StaticContent/` prefix (§5.2).

**v1 — `StaticContent/v1/templates/templates.json`** is a `Template[]`
array. Each entry has the legacy fields the v4 CLI already understands:

```jsonc
{
  "id": "HttpTrigger-JavaScript",
  "files": {
    "index.js": "module.exports = async function (context, req) { ... }",
    "function.json": "{ \"bindings\": [ ... ] }",
    "sample.dat": "..."
  },
  "function": { "bindings": [ { "type": "httpTrigger", ... } ] },
  "metadata": {
    "defaultFunctionName": "HttpTrigger",
    "description": "$HttpTrigger_description",         // resource key
    "name": "HTTP trigger",
    "language": "JavaScript",
    "category": [ "$temp_category_core", "$temp_category_api" ],
    "categoryStyle": "http",
    "enabledInTryMode": true,
    "userPrompt": [ "connection", "httpTrigger-route", "httpTrigger-authLevel" ]
  }
}
```

> `userPrompt[]` references prompt ids defined in
> `StaticContent/v2/bindings/userPrompts.json` — yes, v1 templates
> reference v2 prompts. This is intentional in the upstream bundle:
> the prompt catalog is unified across model versions.

**v2 — `StaticContent/v2/templates/templates.json`** is a `NewTemplate[]`
array used by the Python v2 programming model. Each entry carries the
job/action DSL:

```jsonc
{
  "name": "Blob trigger",
  "description": "$BlobTrigger_description",
  "programmingModel": "v2",
  "language": "python",
  "jobs": [
    {
      "name": "Create New Project with BlobTrigger function",
      "type": "CreateNewApp",
      "inputs": [
        {
          "assignTo": "$(APP_FILENAME)",
          "paramId": "app-fileName",
          "defaultValue": "function_app.py",
          "required": true
        }
        // ... more input entries
      ]
    }
  ]
}
```

**Supporting files** (paths are bundle-source; workload-side drops the `StaticContent/` prefix):

- `StaticContent/v1/bindings/bindings.json` — binding catalog (v1).
- `StaticContent/v2/bindings/userPrompts.json` — `UserPrompt[]`, defines
  validators / enums / labels referenced by `paramId` from templates and
  `metadata.userPrompt`.
- `StaticContent/<v1|v2>/resources/Resources.json` (+ `Resources.<locale>.json`)
  — i18n strings the templates and prompts reference via `$key` syntax.

**Schema authority.** The templates workload owns its schema. The
bundle's schemas are the baseline; the workload-side schema may
diverge as the CLI's needs evolve. The build pipeline (§6.1, §6.2)
applies the divergences when extracting content from the bundle
source.

## 6. Template Source

### 6.1 Node templates — bundle StaticContent

The Node templates workload **does not reauthor templates**. It pulls
its content at workload build time from the **Functions extension
bundle CDN**, the same source the v4 CLI's `func new` resolves
templates from today. Specifically: the templates workload package
embeds a filtered snapshot of one bundle's `StaticContent/` directory
tree (the layout shown in §5.2).

**Source bundle.** Each templates workload package targets one of the
three published bundle channels (channel ↔ bundle id mapping in §4.4.2).

**Build pipeline.**

1. The workload csproj declares two MSBuild properties:
   - `$(SourceBundleId)` — one of the three bundle ids.
   - `$(SourceBundleVersion)` — the bundle version to snapshot (e.g.
     `4.30.0`). Recorded as workload-build provenance only; **not**
     used to derive the templates pkg version (see §4.4.2).
2. A `FetchBundleStaticContent.targets` target downloads
   `<SourceBundleId>/<SourceBundleVersion>` from the bundle CDN and
   extracts only the `StaticContent/` subdirectory of the bundle's
   payload.
3. A filter pass removes per-language template entries that don't
   belong to this stack: keep `Template` entries with
   `metadata.language ∈ {"JavaScript", "TypeScript"}` from
   `v1/templates/templates.json`; drop everything else.
   Bindings and resources are language-agnostic — copied.
4. `Pack` places the filtered tree under `tools/any/content/` of the
   templates workload nupkg, stripping the bundle's outer
   `StaticContent/` wrapper so the layout in §5.2 is achieved.

Build-time fetch keeps `func new` offline-at-invocation. The bundle
CDN is contacted **once per workload republish**, not per user
invocation.

### 6.2 Python templates — bundle StaticContent

Same mechanism as §6.1, filtered for Python.

- **v1 templates:** keep `Template` entries with
  `metadata.language == "Python"` from
  `v1/templates/templates.json`.
- **v2 templates:** keep `NewTemplate` entries with
  `language == "python"` from `v2/templates/templates.json`
  (the Python v2 programming model lives here).
- Bindings and resources for both v1 and v2 are copied
  (language-agnostic).

The bundle's `v2/bindings/userPrompts.json` is included in the Python
workload because Python v1 templates also reference v2 prompts via
their `metadata.userPrompt[]` lists (see §5.4 note).

### 6.3 DotNet templates — NuGet pin

DotNet does not fetch templates at workload build time. Instead:

1. The DotNet templates workload ships only `dotnet-templates.json` +
   `source.json` (§5.3).
2. `source.json` pins a specific version of
   `Microsoft.Azure.Functions.Worker.ItemTemplates` (the upstream
   `dotnet new` template package).

At `func new` time, the CLI core consults `source.json`, provisions
the pinned NuGet pkg into the dotnet template hive, and dispatches
templates from `dotnet-templates.json` to `dotnet new <id>`. The
CLI-side mechanics are out of scope; see the `func new` CLI spec.

The DotNet templates workload is therefore much smaller (KB-scale:
just the two JSON files). The pin gives the CLI a deterministic,
reviewable template version.

Open question (§8): when offline at `func new` time, can the CLI
fall back to whatever templates are already in the hive even if
they differ from `source.json`'s pin?

## 7. Compatibility & Migration

### 7.1 v4 → v5

- `func extensions install` removal: already removed in the v5 CLI;
  templates workload does not re-introduce it. Templates that
  formerly relied on `Template.Metadata.Extensions` (a NuGet pkg
  list) should be migrated to assume the extension bundle is the
  source of truth.
- The legacy `func new` command from v4 is replaced by the v5 CLI's
  built-in `func new` consuming this workload model.

### 7.2 Within v5 (templates workload revisions)

Newer templates workload pkg versions install side-by-side with
older ones. For v1, installs are explicit-version (§4.4.2); how the
CLI picks which installed version to use at `func new` time is an
open question (§8).

## 8. Open Questions

_None at this time._

### Resolved

- ✅ **`kind: content`, not `kind: workload`.** No DI / no
  contribution points. Settled at this draft.
- ✅ **One workload per stack, not a single mega-workload.**
  §4.1.
- ✅ **`func new` lives in CLI core, not in the workload.** §1.
- ✅ **Templates pkg version is independent of bundle version**, for
  every stack. The only version-axis relationship between a templates
  workload and the extension bundle is `minBundleVersion`. §4.4.1,
  §4.4.2.
- ✅ **Templates packaged at workload build time** for Node/Python
  (CDN fetch becomes a build dependency), **at `func new` time
  via `dotnet new`** for DotNet (NuGet pin). §6.
- ✅ **Schema is CLI-owned**, derived from but not identical to the
  bundle baseline. Minor divergences expected over time. §5.4.
- ✅ **Min-bundle storage:** sibling manifest `content/templates-workload.json`
  (authoritative) + nuspec `minBundle:<range>` tag (discoverability) +
  `workload.json` description sentence (human-readable). Workload-wide
  granularity for v1. §4.4.4, §5.1, §5.2.
- ✅ **`source.json` schema (DotNet only):** `{ kind: "nuget",
  packageId, version }`. The `kind` discriminator leaves room for
  future variants (e.g. embedded) without committing to one. §5.3.
- ✅ **Partition by stack, not by language.** One workload per stack
  (Node, Python, DotNet). Language is a per-template property
  (`metadata.language`) within a stack's workload — Node ships both
  JS and TS templates in one package, etc. §4.1.
- ✅ **Templates channel auto-matches the project's bundle channel.**
  At `func new`, the CLI picks the highest installed templates pkg
  whose prerelease label matches the channel of the project's
  `host.json` `extensionBundle.id`. No explicit `--templates-channel`
  flag, no `.func/config.json` pin, no cross-channel fallback. §4.4.2.
