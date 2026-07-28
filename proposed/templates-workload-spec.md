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
  CLI performs no network I/O for templates at invocation time. Template
  payloads are obtained at workload **build time**: Node/Python embed a
  filtered bundle StaticContent snapshot; DotNet hydrates
  `dotnet-templates.json` from the pinned NuGet template pkg and pins
  the same pkg in `source.json`, which `func workload install`
  provisions into the dotnet template hive (no NuGet I/O at `func new`
  time) — see §6.
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
| **`dotnet-templates.json`** | The DotNet templates workload's fully-hydrated catalog + per-template option index under `content/`. Built at workload pack time from each upstream `template.json` (+ optional `dotnetcli.host.json`); drives `func templates list` and `func new --template <id> --help` fully offline from the workload payload. Distinct from the bundle-derived `templates.json` schema used by Node and Python (§5.3, §5.3.1). |
| **Min bundle version** | The lowest extension-bundle version a Node/Python templates workload is known to be compatible with, expressed as a NuGet version range. |
| **Source bundle** | The extension-bundle release (id + version) whose `StaticContent/v2` sub-tree is the upstream source for a Node/Python templates workload's payload. Recorded as build-time provenance; the templates pkg version is independent of the source bundle version (§4.4.1, §4.4.2). |
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
| Workload payload format | `v2/{bindings,resources,templates}/` | `v2/{bindings,resources,templates}/` | `dotnet-templates.json` + `source.json` (NuGet pin) |
| Template source at workload build time | **In-repo static content** (`src/Workloads/Templates/Node/content/v2/`), ported from the v4 Node templates in core-tools `main` via a converter script. The upstream extension bundle does not yet publish v2 Node entries. | Extension-bundle CDN | `Microsoft.Azure.Functions.Worker.ItemTemplates` NuGet pkg (fetched at build to hydrate `dotnet-templates.json`) |
| Per-template source files in workload? | Yes (inline in `files` map) | Yes (inline in `files` map) | No — provisioned into the dotnet template hive from `source.json`'s NuGet pin (§6.3) |
| Catalog + per-template option metadata in workload? | Inline in each `NewTemplate` entry | Inline in each `NewTemplate` entry | Yes — `dotnet-templates.json` is fully hydrated at workload build time from the pinned NuGet pkg's `template.json` files (§5.3, §6.3) |
| Channel axis (stable/preview/experimental)? | **Yes**, with **per-channel template subsetting**: pack time fetches `bin/extensions.json` from the channel's latest listed bundle (`index.json` + HTTP-Range zip extraction) and drops templates whose required bindings aren't in the channel (§6.1). | Yes (matches bundle channel id) | No (stable only; upstream NuGet preview signals propagate via `source.json`) |
| Templating engine in CLI | v2 schema engine (jobs / actions DSL) | v2 schema engine (jobs / actions DSL) | Catalog + `--help` from `dotnet-templates.json`; scaffold via `dotnet new <id>` shell-out (§5.3) |

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
channel's `v2/` content (§6.1, §6.2). The mapping is recorded
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
- **Tags:** `kind:content alias:<stack>-templates func-workload`.
  Node and Python workloads additionally carry a min-bundle tag,
  e.g. `minBundle:[4.18.0, )` (§4.4.4).
- **No `<dependencies>`** in the nuspec. Templates workloads are
  self-contained; the min-bundle dependency is not expressed as a
  NuGet dependency (§4.4.4).

The `workload.json` at the package root carries the manifest:

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "content",
  "displayName": "Python function templates",
  "description": "Function-scaffold templates for Python Azure Functions projects (v2 programming model)."
}
```

`displayName` and `description` surface in `func workload list`. The
`description` is kept short and bundle-agnostic; the authoritative
min-bundle constraint lives in `content/templates-workload.json`
(§4.4.4), not in the `workload.json` description.

### 5.2 On-disk layout — Node and Python

Both stacks ship template content under `tools/any/content/` with a
single `v2/` directory at the root. v1 (legacy) programming-model
templates are not shipped in v5 templates workloads. The CLI core
reads from `v2/` directly.

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
            └── v2/                          ← v2 programming model
                ├── bindings/
                │   └── userPrompts.json     ← UserPrompt[]
                ├── resources/
                │   ├── Resources.json        ← en-US default
                │   ├── Resources.de-DE.json
                │   ├── Resources.fr-FR.json
                │   └── …                     ← i18n siblings
                └── templates/
                    └── templates.json        ← NewTemplate[] (jobs/actions)
```

**Per-stack content of these files:**

- The **Node** workload ships `v2/templates/templates.json` populated
  with Node-language `NewTemplate` entries (`language: "javascript"`,
  `"typescript"`); `v2/bindings/` and `v2/resources/` are language-
  agnostic and copied from the bundle source. **Status:** the
  extension bundle does not yet publish v2 templates for Node, so the
  Node v2 content pipeline is tracked as a follow-up (§6.1); the
  shipped Node workload payload may be empty or transitional until
  that work lands.
- The **Python** workload ships `v2/` (`NewTemplate` jobs/actions for
  the v2 programming model, `language: "python"`) filtered from the
  bundle's `StaticContent/v2` sub-tree. v1 (legacy) Python templates
  are not shipped.

**Note on file representation.** Templates in `v2/templates/templates.json`
carry their per-template files **inline** as a `files: { "<filename>":
"<contents>" }` map within each template object. There is no separate
on-disk file tree under `templates/`. The CLI core materialises
individual files at `func new` time by reading these inline strings.

**Mapping to the bundle source.** This layout is a filtered, repacked
mirror of the bundle's `StaticContent/v2` sub-tree. The bundle is the
upstream source; the templates workload strips the bundle's
`StaticContent/` wrapper since the workload package's
`tools/any/content/` already plays that role. The bundle channel and
the templates workload channel correspond 1:1 (§4.4.2).

### 5.3 On-disk layout — DotNet

Template source files are **not** in the workload payload, and the DotNet
workload does **not** use the bundle StaticContent layout (DotNet has no
extension-bundle dependency — §4.4.3). The DotNet workload instead carries a
**fully-hydrated catalog + option index** (`dotnet-templates.json`) generated at
workload build time from the upstream NuGet template package, plus the NuGet
pin that drives scaffold-time hive provisioning:

```
Azure.Functions.Cli.Workloads.Templates.DotNet.<version>.nupkg
├── workload.json                            (kind: content)
├── README.md
├── release_notes.md
└── tools/
    └── any/
        └── content/
            ├── dotnet-templates.json        ← hydrated at workload build time
            │                                  from each template's template.json
            │                                  (+ dotnetcli.host.json). Drives
            │                                  `func templates list` and
            │                                  `func new --template <id> --help`
            │                                  fully offline. Schema below.
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

#### 5.3.1 `dotnet-templates.json` schema

`dotnet-templates.json` is the canonical CLI-side index for the DotNet stack.
It is built once at workload pack time by projecting each
`*/.template.config/template.json` (+ optional sibling `dotnetcli.host.json`)
from the pinned NuGet template package into a CLI-friendly record. Every field
needed to render catalog rows and to hydrate `func new --template <id> --help`
options is present, so both commands serve **fully offline, deterministically,
from the workload payload** — no NuGet I/O, no `dotnet new` shell-out on read
paths. Only the actual scaffold (`func new <template>`) shells out to
`dotnet new`, and even that is offline once the hive is provisioned at install
time (see §6.3).

```jsonc
{
  "$schema": "https://aka.ms/func-workloads/dotnet-templates/v1/schema.json",
  "sourcePackage": {
    "id":      "Microsoft.Azure.Functions.Worker.ItemTemplates.NetCore",
    "version": "4.0.5569"
  },
  "templates": [
    {
      // ── Catalog row (powers `func templates list`) ───────────────────
      "id":              "http",                                  // shortNameList[0]
      "shortNames":      ["http"],                                // full alias list
      "identity":        "Azure.Function.CSharp.HttpTrigger.2.x", // stable, used to dispatch dotnet new
      "groupIdentity":   "Azure.Function.HttpTrigger",            // dedupes C#/F# variants
      "name":            "HttpTrigger",
      "description":     "Creates an HTTP-triggered function.",
      "author":          "Microsoft",
      "language":        "C#",                                    // tags.language
      "type":            "item",                                  // tags.type
      "classifications": ["Azure Function", "Trigger", "Http"],   // triggers the "TRIGGER" column
      "defaultName":     "HttpTriggerCSharp",
      "constraints":     [ /* mirrored from template.json `constraints` */ ],

      // ── Option hydration (powers `func new --template http --help`) ──
      "parameters": [
        {
          "name":         "namespace",
          "description":  "namespace for the generated code",
          "dataType":     "string",
          "defaultValue": "Company.Function",
          "isRequired":   false,
          "isHidden":     false
        },
        {
          "name":         "AccessRights",
          "displayName":  "Authorization level",
          "description":  "Authorization level controls whether the function requires an API key …",
          "dataType":     "choice",
          "defaultValue": "Function",
          "choices": [
            { "value": "Function",  "description": "Function"  },
            { "value": "Anonymous", "description": "Anonymous" },
            { "value": "Admin",     "description": "Admin"     }
          ],
          "allowMultipleValues": false,
          "shortNameOverride":   null,    // from dotnetcli.host.json (if present)
          "longNameOverride":    null
        }
        // `bind`, `derived`, `computed`, `generated`, and hidden symbols are
        // not user-facing options and are excluded from `parameters[]`.
      ]
    }
  ]
}
```

**Field provenance.** Every field is derivable from the upstream NuGet
template package without re-running the dotnet template engine at scaffold
time, mirroring the surface exposed by `ITemplateInfo` / `ITemplateMetadata`
and (for `parameters[]`) `CliTemplateParameter.GetOption(...)`:

| Schema field | Source on the upstream template |
|---|---|
| `id`, `shortNames` | `template.json` `shortName` |
| `identity` | `template.json` `identity` |
| `groupIdentity` | `template.json` `groupIdentity` |
| `name`, `description`, `author` | `template.json` `name` / `description` / `author` |
| `language`, `type` | `template.json` `tags.language` / `tags.type` |
| `classifications` | `template.json` `classifications` |
| `defaultName` | `template.json` `defaultName` |
| `constraints` | `template.json` `constraints` |
| `parameters[].name` | `template.json` `symbols.<key>` |
| `parameters[].dataType` | `symbols.<key>.datatype` |
| `parameters[].defaultValue` | `symbols.<key>.defaultValue` |
| `parameters[].choices[]` | `symbols.<key>.choices[]` |
| `parameters[].isRequired` | derived from `symbols.<key>.isRequired` / precedence |
| `parameters[].isHidden` | `dotnetcli.host.json` `hiddenParameterNames` ∪ implicit/disabled precedence |
| `parameters[].displayName` | `symbols.<key>.displayName` |
| `parameters[].shortNameOverride`, `parameters[].longNameOverride` | `dotnetcli.host.json` `symbolInfo[].shortName` / `longName` |

**Symbol filter.** Only `type: parameter` symbols are projected into
`parameters[]`. `bind` (host-supplied values like `HostIdentifier`), `derived`,
`computed`, and `generated` symbols are excluded — they're engine- or
host-resolved, not user-facing. Symbols whose precedence is `Implicit` or
`Disabled` are also excluded.

**Localization.** v1 hydrates **en-US only**. The schema reserves a future
`localizations` key for per-locale `description` / `choices[].description`
overrides without requiring a schema bump.

**Scaffold path.** At `func new --template <id>` time, the CLI core dispatches
`dotnet new <shortName> --<param> <value> …` against the dotnet template hive
provisioned from the `source.json` pin (§6.3). The hydrated `parameters[]` is
the canonical source for option names, aliases, and defaults; the engine
only renders the template content.

### 5.4 Templates and binding schemas

The templates workload uses a **CLI-owned schema** derived from the
Functions extension bundle's `StaticContent/v2/` schema. Minor
modifications from the bundle baseline are expected (e.g. fields the
bundle doesn't have, fields the CLI doesn't consume that may be
omitted). One top-level shape applies (v2 programming model). v1
(legacy) templates are not shipped in v5 templates workloads.

> Paths in the schema examples below are **bundle-source** paths.
> Workload-side paths drop the `StaticContent/` prefix (§5.2).

**v2 — `StaticContent/v2/templates/templates.json`** is a `NewTemplate[]`
array used by the v2 programming model. Each entry carries the
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

- `StaticContent/v2/bindings/userPrompts.json` — `UserPrompt[]`, defines
  validators / enums / labels referenced by `paramId` from templates.
- `StaticContent/v2/resources/Resources.json` (+ `Resources.<locale>.json`)
  — i18n strings the templates and prompts reference via `$key` syntax.

**Schema authority.** The templates workload owns its schema. The
bundle's v2 schema is the baseline; the workload-side schema may
diverge as the CLI's needs evolve. The build pipeline (§6.1, §6.2)
applies the divergences when extracting content from the bundle
source.

## 6. Template Source

### 6.1 Node templates — static in-repo content

The Node templates workload **does not** snapshot template content from
the extension-bundle CDN. The upstream bundle's
`StaticContent/v2/templates/templates.json` does not yet publish Node
entries (only Python). The Node v2 template content is therefore
authored **statically in this repo** under
`src/Workloads/Templates/Node/content/v2/`. The build pipeline packs
these files directly into the nupkg at `tools/any/content/v2/`. No
network access is required at pack time beyond the standard NuGet
restore, so the workload builds cleanly under strict network-isolation
CI policies.

**Authoring source.** The static `content/v2/templates/templates.json`
is hand-curated against the v2 `NewTemplate` schema (see
[v2 template engine schema](https://github.com/Azure/azure-functions-templates/tree/dev/Docs)).
Each entry follows the same shape:

- `id`: stack-unique (e.g. `HttpTrigger-JavaScript`).
- `language`: `"node"` for both JS and TS variants (the v2 schema
  enumerates language as `dotnet`/`node`/`python`/`powershell`; the
  JS/TS distinction is encoded in the `id`).
- `programmingModel`: `"v2"` (refers to the **template-engine schema**,
  not the Node runtime programming model — which is itself v4 /
  decorators-based).
- `jobs`: one `CreateNewApp` job per template carrying:
  - an input for the function name (`paramId: trigger-functionName`)
    with a stable default,
  - one further input per binding-config field, with the paramId
    resolved to a context-aware `<triggerContext>-<param>` form in the
    workload's `content/v2/bindings/userPrompts.json`.
- Top-level `actions`: a `GetTemplateFileContent` +
  `WriteToFile` pair that materialises the function file at
  `src/functions/$(FUNCTION_NAME_INPUT).<js|ts>`.
- `files`: the function-body content, with `$(FUNCTION_NAME_INPUT)`
  tokens for the function name. Per-binding literal values
  (queue name, connection string, blob path, etc.) currently match
  the v4 defaults; per-binding tokenisation is a follow-up (§8).

**Build pipeline (static mode).**

```msbuild
<TemplatesContentSource>static</TemplatesContentSource>
<IncludeV1Templates>false</IncludeV1Templates>
<IncludeV2Templates>true</IncludeV2Templates>
<FilterTemplatesByBundleChannel>true</FilterTemplatesByBundleChannel>
```

is set in `Workloads.Templates.Node.csproj`. The shared
`eng/build/Workloads.Templates.targets` recognises the `static` value
of `$(TemplatesContentSource)` and:

1. Skips every CDN `<DownloadFile>` and the language-filter scripts.
2. Validates that `content/v2/templates/templates.json` exists under
   the project directory.
3. Adds every `content/v2/**/*.json` (and `content/v1/**/*.json` when
   `$(IncludeV1Templates)` is true) as a `<None Pack="true"
   PackagePath="tools/any/content/v2/" />` item — minus the build-time-
   only manifest `content/v2/templates/_bindings.json` (excluded).
4. Still generates the `templates-workload.json` sibling manifest from
   `$(MinBundleVersionRange)`, identical to CDN-mode behaviour.

**Per-channel subsetting** is **on** for the Node workload via
`$(FilterTemplatesByBundleChannel)=true`. At pack time the build
cross-references the master `content/v2/templates/templates.json`
against the active `$(TemplatesChannel)`'s authoritative extension
bundle:

1. **Resolve the listed version.** `eng/scripts/fetch-bundle-extensions-json.ps1`
   fetches `index.json` from CDN for the active channel
   (`https://cdn.functions.azure.com/public/ExtensionBundles/<id>/index.json`,
   id mapping per §4.4.2). It picks the **last `4.x` entry** — array
   order in `index.json` is publication order, so the tail is the
   newest listed bundle. Versions present on CDN but **not** listed in
   `index.json` are unlisted and must not be consumed.
2. **Extract `bin/extensions.json` via HTTP Range.** The script issues
   two Range requests against `<id>/<version>/<id>.<version>.zip`: one
   for the End-of-Central-Directory and central directory (last
   ~64 KB), then one for the `bin/extensions.json` entry's local file
   header + compressed payload. Total transfer is on the order of
   10–20 KB instead of the full 150–250 MB bundle, so per-build cost
   is negligible.
3. **Filter against `_bindings.json`.** `eng/scripts/filter-node-templates-by-bundle.ps1`
   reads:
   - the master `templates.json` (NewTemplate[]),
   - the committed `content/v2/templates/_bindings.json` (template
     id → string[] of required binding names; empty array means the
     template has no extension dependency and is always included),
   - the channel's `bin/extensions.json`.

   For each template entry, every required binding must appear (case-
   insensitively) in some `extensions[].bindings[]` array of the
   channel's `extensions.json`. Templates whose binding set isn't fully
   satisfied are dropped. The filtered file is staged at
   `$(IntermediateOutputPath)templates\v2\templates\templates.json`
   and packed in place of the source file.

If a new template is added to `templates.json` without a corresponding
entry in `_bindings.json`, the filter script fails the build — so
per-channel subsetting can't silently drop content.

**Cross-reference snapshot at the time of this revision** (latest
listed `4.x` per channel):

| Channel | Listed bundle | Templates kept |
|---|---|---|
| stable | `Microsoft.Azure.Functions.ExtensionBundle` `4.32.0` | **31 of 33** — drops `McpPromptTrigger-{Javascript,Typescript}` (`mcpPromptTrigger` binding not yet in stable) |
| preview | `Microsoft.Azure.Functions.ExtensionBundle.Preview` `4.42.0` | **33 of 33** — full set |
| experimental | `Microsoft.Azure.Functions.ExtensionBundle.Experimental` `4.6.0` | **31 of 33** — same drop as stable |

### 6.2 Python templates — bundle StaticContent

Same mechanism as §6.1 (target), filtered for Python and shipping
today.

- **v2 templates:** keep `NewTemplate` entries with
  `language == "python"` (case-insensitive) from
  `v2/templates/templates.json` (the Python v2 programming model
  lives here).
- **v1 templates:** **not shipped.** The Python templates workload
  drops v1 (legacy programming model) content; users on v1 should
  migrate to v2 (see §7.1).
- Bindings (`v2/bindings/userPrompts.json`) and resources
  (`v2/resources/Resources*.json`) are copied (language-agnostic).

The bundle's `v2/bindings/userPrompts.json` is included in the Python
workload because v2 Python templates reference its prompt definitions
via `paramId` references inside `jobs[].inputs[]`.

### 6.3 DotNet templates — hydrate-from-pinned-NuGet

DotNet does not author templates and does not ship the template *source
files* in the workload payload. Instead, the workload build pipeline:

1. **Pin the source.** The workload csproj declares two MSBuild properties:
   - `$(SourceItemTemplatesPackageId)` — `Microsoft.Azure.Functions.Worker.ItemTemplates`
     (or its `.NetCore` / `.NetFx` variant).
   - `$(SourceItemTemplatesVersion)` — the upstream template-package version
     to pin (e.g. `4.0.5569`).
2. **Restore the pinned pkg at build time.** A `PackageDownload` (the same
   pattern `eng/build/Templates.targets` uses today) drops the unpacked
   contents into the workload's `obj/`.
3. **Hydrate `dotnet-templates.json`.** A build target walks
   `*/.template.config/template.json` (+ optional sibling
   `dotnetcli.host.json`) inside the unpacked pkg and projects each Functions
   template into a `dotnet-templates.json` entry (schema in §5.3.1). Selection
   criteria:
   - `classifications` contains `"Azure Function"` (or `groupIdentity`
     starts with `"Azure.Function."`), **and**
   - `tags.type == "item"`.

   The symbol filter described in §5.3.1 strips `bind` / `derived` /
   `computed` / `generated` / hidden symbols before emitting `parameters[]`.
4. **Emit `source.json`.** The pinned package id + version are written into
   `content/source.json` so the CLI core knows which NuGet pkg to provision
   into the dotnet template hive at install time (step 5).
5. **Pack.** Both files land under `tools/any/content/` of the templates
   workload nupkg (§5.3). No template *source* files are packed.

At **`func workload install` time**, the CLI core reads `source.json` and
provisions the pinned NuGet template pkg into the dotnet template hive it
manages. After install, both read paths (`func templates list`,
`func new --template <id> --help`) and the scaffold path
(`func new --template <id>`) are offline-deterministic. The CLI-side
mechanics of hive provisioning are out of scope for this spec; see the
`func new` CLI spec.

The DotNet templates workload is therefore much smaller than Node/Python
(KB-scale: just `dotnet-templates.json` and `source.json`), and reviewers
can audit the hydrated catalog and parameter set directly from the workload
package without unpacking the upstream NuGet pkg.

## 7. Compatibility & Migration

### 7.1 v4 → v5

- `func extensions install` removal: already removed in the v5 CLI;
  templates workload does not re-introduce it. Templates that
  formerly relied on `Template.Metadata.Extensions` (a NuGet pkg
  list) should be migrated to assume the extension bundle is the
  source of truth.
- The legacy `func new` command from v4 is replaced by the v5 CLI's
  built-in `func new` consuming this workload model.
- **v1 (legacy) programming-model templates are not shipped in v5
  templates workloads.** Users still authoring against the v1
  programming model should either migrate to v2 (the v5 baseline
  shape — `NewTemplate[]` with jobs/actions) or stay on v4 tooling
  until migration is complete. The CLI does not provide a
  v5-side bridge for v1 template content.

### 7.2 Within v5 (templates workload revisions)

Newer templates workload pkg versions install side-by-side with
older ones. For v1, installs are explicit-version (§4.4.2); how the
CLI picks which installed version to use at `func new` time is an
open question (§8).

## 8. Open Questions

- **Per-binding tokenisation of Node template file contents.** The v4
  Node templates rely on the v4 engine's userPrompt-name-matching
  substitution: e.g. a `connection: ''` field in the JS source is
  replaced based on the userPrompt named `connection`. The v2 schema
  uses explicit `$(VAR)` tokens. The v4→v2 converter only handles the
  function-name token mechanically (`%functionName%` →
  `$(FUNCTION_NAME_INPUT)`); per-binding literals (queue name,
  connection string, blob path, etc.) currently match v4 defaults.
  Producing fully-tokenised v2 files requires per-template hand
  tokenisation. Tracked as a follow-up against §6.1.

### Resolved

- ✅ **Per-channel template subsetting for the Node workload.** At
  pack time the build cross-references the master Node `templates.json`
  against the active channel's authoritative extension bundle: the
  latest **listed** version (per the channel's CDN `index.json`) has
  its `bin/extensions.json` extracted via HTTP Range from
  `<id>.<version>.zip`, and templates are filtered against a committed
  `_bindings.json` map (template id → required binding names). The
  filtered subset is what gets packed for the channel-tagged nupkg.
  Versions present on CDN but not listed in `index.json` are unlisted
  and not consumed. Cross-reference snapshot at this revision: stable
  31 / 33, preview 33 / 33, experimental 31 / 33 (the two `McpPromptTrigger`
  variants drop in stable + experimental because the
  `mcpPromptTrigger` binding hasn't propagated to those channels yet)
  — see §4.3 / §6.1.

- ✅ **Node templates ship as in-repo static v2 content.** The
  upstream extension bundle does not yet publish v2 Node templates,
  so the Node templates workload owns its content under
  `src/Workloads/Templates/Node/content/v2/`. The static `templates.json`
  is hand-curated against the v2 `NewTemplate` schema. No CDN download
  of templates at pack time (§4.3, §6.1).

- ✅ **Node and Python templates workloads ship v2-only.** Both
  stacks ship `NewTemplate[]` v2 programming-model content under
  `tools/any/content/v2/`. v1 (legacy) programming-model templates
  are not shipped. Users still on v1 migrate to v2 or stay on v4
  tooling (§5.2, §5.4, §6.1, §6.2, §7.1).

- ✅ **`kind: content`, not `kind: workload`.** No DI / no
  contribution points. Settled at this draft.
- ✅ **One workload per stack, not a single mega-workload.**
  §4.1.
- ✅ **`func new` lives in CLI core, not in the workload.** §1.
- ✅ **Templates pkg version is independent of bundle version**, for
  every stack. The only version-axis relationship between a templates
  workload and the extension bundle is `minBundleVersion`. §4.4.1,
  §4.4.2.
- ✅ **Templates packaged at workload build time** for every stack.
  Python embeds a filtered bundle StaticContent snapshot (§6.2);
  Node packs in-repo static v2 content authored from the v4 Node
  templates in core-tools `main` via a converter script (§6.1);
  DotNet hydrates a catalog + parameter index (`dotnet-templates.json`)
  from the pinned NuGet template pkg's `template.json` files (§6.3).
  No CDN call from `func new` itself.
- ✅ **`dotnet-templates.json` schema (DotNet only).** Fully-hydrated
  catalog rows + `parameters[]` per template (§5.3.1). Drives
  `func templates list` and `func new --template <id> --help` offline from
  the workload payload — the upstream NuGet pkg is consulted only to
  produce content during the workload's build. Field provenance traces
  to the dotnet template engine's `ITemplateInfo` /
  `ITemplateMetadata` / `CliTemplateParameter` surface.
- ✅ **Offline at `func new` time for DotNet.** Catalog read and
  `--template X --help` are always offline (served from
  `dotnet-templates.json`). The scaffold path is also offline once the
  pinned pkg from `source.json` has been provisioned into the dotnet
  template hive — the CLI core does that provisioning **at
  `func workload install` time**, not lazily at `func new` time. This
  preserves the §1 "offline at invocation" goal across all paths and
  removes the earlier open question about lazy fallback to whatever
  templates are already in the hive.
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
