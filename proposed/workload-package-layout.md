# Workload Package Layout

> **Status:** Draft v0.9. Companion to [`workload-spec.md`](./workload-spec.md).
> This document defines the on-disk and on-feed layout of the NuGet package
> that ships a Func CLI workload. The spec defines *what* a workload is and
> *how* the CLI loads it; this document defines *how the bytes are arranged*
> inside the `.nupkg`.
>>
> **Spec coordination required.** The `kind` rename, the meta concept,
> and the `installedExplicitly` flag affect `workload-spec.md` §5.3,
> §6.1, §6.2, and §10.1. They are tracked here so the spec PR can
> ride along with the layout iteration.

## 1. Goals

- Pick a single, opinionated package layout so workload authors don't have
  to invent one and the install pipeline doesn't have to support multiple
  shapes.
- Re-use **existing NuGet folder conventions** wherever they already mean
  the right thing, so that authors with NuGet background recognise the
  layout and tooling (`dotnet pack`, NuGet feed UIs, `nuget verify`)
  understands the package without custom configuration.
- Keep the layout **portable** (RID-agnostic at the workload level).
  Per-RID assets are an implementation detail of a workload's transitive
  dependencies, not a fork in the package layout the CLI has to reason
  about.
- Make the package readable by tooling that doesn't know about Func
  workloads (a developer cracking the `.nupkg` open in 7-zip, a feed UI,
  a security scanner).

## 2. Non-goals

- Defining the *contents* of templates or other workload payload formats.
  The layout reserves a folder for them; format is workload-internal.
- Defining the install pipeline's on-disk layout under
  `~/.azure-functions/workloads/<id>/<version>/`. Install can mirror the
  package layout 1:1 or flatten it; that's a separate decision.
- Per-RID workload variants. v1 ships portable workloads only (see §6 in
  `workload-spec.md` and the user direction recorded in §4 below).
- Compile-time consumption of workload assemblies by other projects.
  Workloads are CLI extensions, never `<PackageReference>`-d by
  another csproj.

## 3. Pre-existing patterns considered

NuGet has a handful of well-defined "this folder means X" conventions.
We evaluated each against the workload use case — *a portable .NET
assembly, plus its dependencies and static assets, that the Func CLI
extracts and loads at runtime*.

| Convention                           | What it means                                                      | Fits workloads? |
|--------------------------------------|--------------------------------------------------------------------|-----------------|
| `lib/<tfm>/`                         | Managed assemblies referenced at compile-time and runtime by the consuming project. | Partial. We don't want consumers to `<PackageReference>` workloads. Using `lib/` would create some confusion and potential invalid expectations, even if the package type does not allow consumption from other projects. |
| `ref/<tfm>/`                         | Reference-only assemblies for compile-time.                        | No. Workloads aren't compiled against. |
| `runtimes/<rid>/lib/<tfm>/`          | RID-specific managed assemblies, picked by NuGet at restore.       | No at the workload level (see §4). Yes for transitive deps that ship native bits. |
| `runtimes/<rid>/native/`             | RID-specific native binaries, surfaced to the published app.       | Same as above — only for transitive deps. |
| `content/`, `contentFiles/<lang>/<tfm>/` | Files copied into the consuming project's source tree.         | No. There is no consuming project. |
| `build/<tfm>/<id>.{props,targets}`   | MSBuild logic auto-imported into the consuming project.            | No. |
| `analyzers/dotnet/{cs,vb}/`          | Roslyn analyzers loaded by the C#/VB compiler.                     | No. |
| `tools/<tfm>/<rid>/`                 | A self-contained .NET payload that an external runner extracts and invokes. The package declares `<PackageType>DotnetTool</PackageType>` and the runner (`dotnet tool`) reads `DotnetToolSettings.xml` to discover the entry point. | **Yes — this is the closest match.** The Func CLI plays the same role for `FuncCliWorkload` packages that `dotnet tool` plays for `DotnetTool` packages. |
| `Sdk/Sdk.{props,targets}`            | An MSBuild project SDK loaded by MSBuild's SDK resolver.           | No. Different host. |

The closest pre-existing pattern is **`DotnetTool`**, and the closest
pre-existing **install pipeline** is `dotnet tool install` (with
`dotnet new install` as a corroborating data point). Both .NET SDK
features are NuGet-distributed CLI extensions, and both make the same
shape of choices we make here:

- The package contains a portable .NET payload.
- The runner (`dotnet tool` / `dotnet new` / `func`) extracts the
  package and loads the payload, rather than the .NET SDK linking it
  into another project.
- A package-type marker (`DotnetTool` / `Template` / `FuncCliWorkload`)
  identifies the package as a runner-consumed unit, not a library.
- A **per-package settings file** declares what the runner should
  invoke: `DotnetToolSettings.xml` for `DotnetTool`, `template.json`
  for `Template`, `workload.json` for `FuncCliWorkload` (see §5.4).
  None of the three rely on a runtime scan of assemblies.
- The install code references **only** `NuGet.Protocol` /
  `NuGet.Versioning` / `NuGet.Packaging` / `NuGet.Configuration` /
  `NuGet.Credentials` — *not* `NuGet.Resolver` or
  `NuGet.PackageManagement` — and never calls `RestoreCommand` /
  `RestoreRunner`
  ([`dotnet/sdk:src/Cli/dotnet/ToolPackage/ToolPackageDownloader.cs`](https://github.com/dotnet/sdk/blob/main/src/Cli/dotnet/ToolPackage/ToolPackageDownloader.cs),
  [`dotnet/templating:src/Microsoft.TemplateEngine.Edge/Installers/NuGet/`](https://github.com/dotnet/templating/tree/main/src/Microsoft.TemplateEngine.Edge/Installers/NuGet)).
- Bytes go to a private CLI-managed location
  (`~/.dotnet/tools/.store/<id>/<version>/` for tools,
  `~/.templateengine/.../packages/<id>.<version>.nupkg` for
  templates), **not** the standard NuGet packages cache.
- The package type is enforced as a hard gate at install time
  (`ToolPackageDownloader` throws `ToolPackageNotATool` immediately
  on a missing `<packageType name="DotnetTool"/>`).

We adopt the `tools/` location and the `any` RID slot from
`DotnetTool`, plus the per-package settings-file pattern, but **drop
the `<tfm>/` segment** — the workload's TFM is recorded in its
assembly metadata and `.deps.json`, and the compatibility boundary
that actually matters is the host-shared contract-assembly versions
(see §6), not the folder name. Spending a folder segment on something
the package's own metadata already states buys nothing in v1 and
would commit the install pipeline to TFM matching for a multi-TFM
scenario we don't need.

For two of the three package kinds (`workload` and `content`,
§5.5), we **align** with that SDK precedent on dependency handling:
their `.nuspec` `<dependencies>` is **empty** and they are fully
self-contained, with library `<PackageReference>`s vendored into
`tools/any/` by `dotnet publish` (§5.5, §9.3). The install pipeline
does no transitive resolution and no closure tracking for these
kinds.

Where we **diverge** from the SDK precedent — and the divergence we
have to justify carefully — is the third kind, **`meta` packages**
(§5.5), that bundle multiple workloads under a single id. Both
`dotnet tool install` and `dotnet new install` silently ignore
`.nuspec <dependencies>` and have no transitive-install support at
all. We take a more ambitious position because curated bundles of
workloads (a "Worker Family" meta packaging Node + Python + Java
together, a versioned bundle that bumps several workloads in
lockstep) materially help users. The mechanism is detailed in §5.5
and consciously stays close to the SDK shape: NuGet for transport
+ metadata, Func code for the meta-expansion + uninstall semantics
on top. The resolution is one level deep (no meta-of-meta in v1),
so the install pipeline never grows a transitive dep solver.

## 4. User direction (recorded for context)

From the design conversation:

> *"No RID specific logic in the layout. The workload should always be
> in portable format, with a RID that would be compatible with the host,
> so the host wouldn't have to load the workload from different RID
> folders. The workload itself could have RID specific assets, but that
> would simply be for the runtime dependencies load behavior."*

In layout terms:

- The workload's own assembly is **always portable** and lives under a
  single `tools/any/` folder — `any` is the only RID slot the package
  layout uses. There is **no TFM segment**: the workload's TFM is
  recorded in the assembly's `TargetFrameworkAttribute` and in the
  adjacent `.deps.json`, and binary compatibility with the running
  CLI is determined by the contract-assembly versions (§6), not by a
  folder name. Spending a folder segment on something the package's
  own metadata already states would be cargo-cult NuGet conventions
  for no gain.
- A workload **may** include `runtimes/<rid>/...` assets *inside*
  `tools/any/runtimes/`, exactly as `dotnet publish` arranges them,
  so the .NET runtime resolves RID-specific transitive dependencies
  (managed or native) without the Func CLI having to know about
  them.

> *"Instead of scanning, we expect to have a workloads.json file that
> defines the workload metadata. This would have a relative path to
> the entrypoint assembly we'd ultimately load in the CLI (along with
> the entry point type)."*

In layout terms:

- A required `workload.json` (singular, to disambiguate from the
  global `workloads.json`) sits at the **package root**. It declares
  `entryPoint.assemblyPath` (a path relative to `tools/any/`) and
  `entryPoint.type` (the FQN to instantiate). See §5.4.
- The install pipeline reads this file and **never** reflects on the
  workload's assemblies. The marker attribute survives only as an
  optional compile-time aid (§9.1).

## 5. Recommended layout

```
<PackageId>.<Version>.nupkg
│
├── tools/
│   └── any/                                            # Always 'any'. Workloads are portable.
│       ├── <PackageId>.dll                             # The workload assembly. Implements
│       │                                               # IWorkload. May (optionally) carry
│       │                                               # [assembly: ExportCliWorkload<T>] as
│       │                                               # a compile-time check.
│       ├── <PackageId>.deps.json                       # Standard .NET runtime asset graph.
│       │                                               # Lets the loader resolve managed deps
│       │                                               # and runtimes/<rid>/native/* via the
│       │                                               # AssemblyDependencyResolver. Records the
│       │                                               # workload's TFM.
│       ├── <PackageId>.pdb                             # Portable PDB (recommended; included
│       │                                               # so workload errors surface usable
│       │                                               # stack traces in the CLI).
│       ├── <transitive managed dependencies>.dll       # As emitted by `dotnet publish`.
│       └── runtimes/                                   # OPTIONAL. Present only if a transitive
│           ├── win-x64/                                # dep ships native or RID-specific managed
│           │   ├── lib/net10.0/...                     # bits. Workload-private; resolved by the
│           │   └── native/...                          # .NET runtime, not the CLI.
│           ├── linux-x64/...
│           └── osx-arm64/...
│
│       # Authors may include other workload-internal asset directories
│       # under tools/any/ (e.g., embedded resources). The CLI does not
│       # enumerate them. Whether `func init`/`func new` templates ship
│       # in-package or as a separate `Template`-typed package is open
│       # (§11).
│
├── workload.json                                       # Func-defined per-package manifest.
│                                                       # Declares the entry-point assembly path
│                                                       # (relative to the package root) and the
│                                                       # entry-point type FQN. Read by the install
│                                                       # pipeline; the CLI does not load any
│                                                       # workload assembly during install. See §5.4.
│
├── README.md                                           # Surfaced by NuGet feeds and `func workload list`'s
│                                                       # verbose output. Set via <PackageReadmeFile>.
│
├── icon.png                                            # Set via <PackageIcon>.
│
└── <PackageId>.nuspec                                  # Auto-generated by `dotnet pack`.
                                                        # Required NuGet metadata:
                                                        #   <packageTypes>
                                                        #     <packageType name="FuncCliWorkload" />
                                                        #   </packageTypes>
                                                        #   <tags>alias:<name></tags>      (and optionally func-workload)
```

Plus the standard NuGet envelope (`[Content_Types].xml`, `_rels/`,
`package/`) which `dotnet pack` writes automatically.

### 5.1 Why `tools/`, not `lib/<tfm>/` or `content[Files]/`

`tools/` is a **Func-defined convention** that borrows the folder
shape `DotnetTool` packages use. Generic NuGet tooling does not
recognise `FuncCliWorkload` and will render these as plain packages
in feed UIs; the goal of using `tools/` is not to opt in to special
UI treatment, it's to keep the workload payload out of the asset
paths that PackageReference consumers — or other NuGet-aware
tooling — would pick up unexpectedly.

§3's table lists every NuGet folder convention. This section is the
"why not the obvious alternatives" deep dive for the three folders an
author might reasonably reach for: `lib/`, `content/`, and
`contentFiles/`.

#### `lib/<tfm>/` — the canonical assembly location

PackageReference restore surfaces `lib/<tfm>/<assembly>.dll` as a
compile + runtime reference to any consuming project. A user who
accidentally `<PackageReference>`s a workload package would get the
workload assembly on their build path, which is wrong. The package
type alone does not gate this — restore happens before the consumer
project sees the package type and does not refuse to surface assets
for unknown types. Keeping the workload payload out of `lib/` is a
hard prerequisite, not a preference. `lib/` is also the canonical
shape for `<PackageType>Dependency</PackageType>` packages, which is
exactly the framing `FuncCliWorkload` is trying *not* to take.

#### `content/` — the packages.config-era content path

Files in `content/` were copied into the consuming project's source
tree at install time. The path is **deprecated** under PackageReference;
modern clients largely ignore it (which is why a workload accidentally
placed there wouldn't break a consumer's build), but the deprecation
cuts the other way too: using a deprecated NuGet path for a brand-new
Func feature is a bad signal to authors, scanners, and future tool
integrations. `content/` also has no semantic relationship to "a
runtime payload an external runner extracts and invokes" — its whole
point is "files copied into a consumer project".

#### `contentFiles/<lang>/<tfm>/` — the PackageReference replacement

The modern replacement for `content/`. Files are surfaced to consumers
with `buildAction` / `copyToOutput` / `copyToPublishDirectory` metadata,
wired up by `<contentFiles>` groups in the .nuspec and a `.targets`
import that NuGet auto-imports. Default behaviour without that wiring
*is* to ignore the files — but at that point we'd be using a folder
whose *reason for existing* is to put files into consumer builds, and
relying on the absence of metadata to say "actually, ignore them". A
future PackageReference change to default-include unmarked
`contentFiles/` would silently break the workload contract. `tools/`
says the right thing without needing escape hatches or metadata
gymnastics.

#### Why "templates use `content/`" doesn't generalise

`Template` packages (`<PackageType>Template</PackageType>`, consumed
by `dotnet new install`) ship their template content under `content/`
**by convention only** — the dotnet templating engine doesn't require
it. `NuGetInstaller` recursively scans the entire package for
`.template.config/template.json` markers regardless of folder
([`dotnet/templating:src/Microsoft.TemplateEngine.Edge/Installers/NuGet/NuGetInstaller.cs`](https://github.com/dotnet/templating/tree/main/src/Microsoft.TemplateEngine.Edge/Installers/NuGet)).
Templates can sit in `content/` because nothing about a Template
package contradicts a content folder, not because `content/` carries
useful semantics for the install pipeline. For a workload — which is
primarily a runtime .NET payload with workload-internal data — the
right semantics is `tools/`: a folder defined as "runner-consumed
bytes that PackageReference consumers must not see".

#### Is `tools/` "modern"?

`tools/` originally housed packages.config-era PowerShell hooks
(`init.ps1`, `install.ps1`, `uninstall.ps1`). Those hooks are
deprecated under PackageReference. **But `dotnet tool install`
revived `tools/` for a different purpose** — the .NET tool payload
under `tools/<tfm>/<rid>/` — and that revival is the convention we
align with, not the old PowerShell-hook semantics. `tools/<tfm>/<rid>/`
has shipped with every modern dotnet SDK since .NET Core 2.1 (2018)
and is actively maintained. Choosing `tools/` for workloads picks the
maintained, widely-understood "CLI-extension payload" slot, not the
abandoned scripting one.

#### Other dimensions (vs `lib/`)

| Aspect                                    | `tools/` (chosen)                                   | `lib/` (rejected) |
|-------------------------------------------|-----------------------------------------------------|--------------------|
| Layout `dotnet publish` produces          | 1:1 — publish output drops straight in.             | Requires custom `<PackagePath>` plumbing for the runtime asset graph. |
| Matches `<PackageType>` semantics         | Mirrors `DotnetTool`'s "runner-consumed payload" intent. | `lib/` packages are normally type=`Dependency`, which contradicts `FuncCliWorkload`. |
| Discoverability for non-Func tooling      | Generic feeds render the package as "a NuGet" with no special UI. `func workload search` filters by the `FuncCliWorkload` package type; the `func-workload` tag is a recommended hint for feed UIs. | Same. |

#### Note on custom package types and standard clients

NuGet's docs note that arbitrary custom `<packageType>` values are
not installable by some general-purpose clients (e.g. older
`nuget.exe`, certain Visual Studio flows). This is acceptable here
because workload packages are not meant to be installed by those
clients — only by `func workload install`. We rely on
`FuncCliWorkload` purely as a filter / classifier, not as an
instruction to those clients.

### 5.2 Install-time payload resolution

The install pipeline reads `workload.json` (§5.4) at the package root
to learn what to load — it does **not** scan the package's assemblies.

1. Open the package and read `/workload.json`. Reject the package if
   the file is missing, isn't valid JSON, or has a `$schema` URL the
   CLI doesn't recognise (per spec §10.1, mirrored here for the
   per-package manifest).
2. **Branch on `kind`** (per `workload.json`, §5.4):
   - `kind: workload` (or absent — defaults to workload): proceed to
     step 3.
   - `kind: content`: proceed to step 3 (identical extraction; the
     loader will skip the entry at activation, but the install
     pipeline treats it the same shape on disk).
   - `kind: meta`: hand off to the **meta install flow** in §5.5.
     Metas have no payload of their own; the rest of this section
     describes the per-package payload-resolution path that
     applies to `workload` and `content` kinds only.
3. **Validate manifest shape** (§5.4). For `kind: workload`,
   `entryPoint.assemblyPath` and `entryPoint.type` must both be
   present, the path must not be absolute or contain `..` segments,
   and the file it points to (resolved relative to `tools/any/`)
   must exist inside the package. For `kind: content`, `entryPoint`
   must be absent and the `tools/any/` payload contents are not
   further inspected. Reject the package on any failure.
4. Move the **entire `tools/any/` payload** into the install location
   (`<workload-home>/workloads/<packageId>/<packageVersion>/tools/any/`)
   preserving its internal structure. The workload assembly, its
   `.deps.json`, the `runtimes/` subtree, and any workload-internal
   asset directories must remain adjacent so
   `AssemblyDependencyResolver` can resolve dependencies relative to
   the workload assembly and so workload code can find its data
   files at paths relative to `AppContext.BaseDirectory`. Copy
   `workload.json` itself to the install-dir root next to `tools/`
   so the loader can re-read it without going back to the package.
5. Record the package in the global registry (spec §10.1) with
   `packageId` (lowercased, NuGet-normalized), `packageVersion`,
   `kind`, `aliases` (from `.nuspec` `alias:` tags), `source`, and
   — for `kind: workload` — `entryPoint.assemblyPath` and
   `entryPoint.type` copied verbatim from `workload.json`. New rows
   carry `installedExplicitly: true` when the user invoked
   `func workload install <id>` directly, or `installedExplicitly:
   false` when this row is being created as a member of a meta
   install (§5.5). The registry write is atomic (temp file +
   rename) so a crashed install either leaves the row absent
   (orphan dir, cleaned by `func workload prune`) or fully present.

The full eight-step install pipeline (download, signature
verification, extract, atomic move, registry update, post-install
hooks) is owned by `workload-spec.md` §6.1; this section's contract
is purely "how the bytes are arranged on disk after a successful
install" for `kind: workload` and `kind: content` packages. The
meta-package install flow (download, pre-flight resolve members,
extract each member as if it were an explicit install but with
`installedExplicitly: false`, write the meta's row last) is
detailed in §5.5.

This makes the layout the **install pipeline's contract**, not just
an authoring convenience. The pipeline is allowed to drop everything
outside `tools/any/` and `workload.json` (`README.md`, `icon.png`,
etc.) but must not reorganise what's inside `tools/any/`.

The pipeline does **not**:

- Load any assembly (no `AssemblyLoadContext` spin-up at install).
- Match TFMs at install time. The workload's TFM is recorded in its
  assembly metadata and `.deps.json`; binary compatibility with the
  running CLI is enforced via the contract-assembly versioning
  described in §6, not via folder selection.
- Trust the assembly's `[assembly: ExportCliWorkload<T>]` attribute
  (if present) over `workload.json`. The manifest is the source of
  truth; the attribute is a compile-time aid only (§9).

### 5.3 Why `tools/any/` and not flat `tools/`

The `tools/` segment alone would be enough to keep the payload out
of PackageReference asset paths (§5.1). The trailing `any/` segment
is kept for two specific reasons:

1. **Future-proofing for RID-specific workloads.** The slot exists
   in the established `DotnetTool` convention (`tools/<tfm>/<rid>/`)
   for a reason: a tool may need separate bytes per RID. Workloads
   in v1 are portable (§4) and always populate `tools/any/`. If a
   future workload class needs RID-specific bits — e.g., a workload
   that wraps a native CLI it ships alongside — `tools/win-x64/` can
   sit next to `tools/any/` without a layout migration. With a flat
   `tools/`, the same scenario forces either a sub-folder rename
   (breaking change) or reading the payload's `.deps.json` to pick
   one of several flat sets, both of which are incompatible with
   the "extract the `tools/<rid>/` subtree as-is" install rule
   (§5.2).
2. **Reads as "explicitly portable", not "RID forgotten".** A
   reviewer or scanner looking at the package sees `tools/any/` and
   immediately knows the package author marked the payload as
   cross-platform. A flat `tools/` is silent on the question, and
   tools that already understand `tools/<tfm>/<rid>/` (signing
   pipelines, content scanners, mirroring tools) may misinterpret
   it.

Why **`any`** and not the host's RID for v1: per §4, the workload
assembly is portable. Picking `any` keeps a single folder authors
fill from `dotnet publish`, and lets the install pipeline extract
the same bytes regardless of the user's RID. Any RID-specific
behaviour comes from transitive dependencies and is resolved by the
.NET runtime via the workload's `.deps.json` — the same mechanism
every other portable .NET app relies on.

The cost of the extra segment is one three-letter folder name in
the package layout, generated by the pack target; authors never
type it manually. The benefit is preserving an established slot
that's already understood by NuGet content scanners and pack tools
that know about `tools/<tfm>/<rid>/`.

### 5.4 Workload manifest (`workload.json`)

A single JSON file at the **package root** declares the workload's
entry point. Modeled on `DotnetToolSettings.xml` for `DotnetTool`
packages, but in JSON because every other Func CLI manifest in this
design is JSON.

#### Schema (v1)

The `kind` field discriminates between three package shapes. Default
is `"workload"`; the field may be omitted in normal workload
manifests.

Normal workload (default; runs in the loader, contributes services):

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "workload",
  "entryPoint": {
    "assemblyPath": "Azure.Functions.Cli.Workload.Node.dll",
    "type": "Azure.Functions.Cli.Workload.Node.NodeWorkload"
  }
}
```

Content package (e.g. host runtime — ships files, no entry point,
loader skips):

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "content"
}
```

Meta package (no payload — bundles other workloads via
`.nuspec` `<dependencies>`):

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "meta"
}
```

| Field                       | Required                      | Meaning |
|-----------------------------|-------------------------------|---------|
| `$schema`                   | Required                      | Versioned JSON Schema URL. Same convention as the global registry (spec §10.1). The CLI rejects packages whose `$schema` URL it doesn't recognise; the manifest is **never** parsed partially (see "Forward compatibility" below). |
| `kind`                      | Optional (default `"workload"`) | One of `"workload"`, `"content"`, or `"meta"`. **`workload`** packages carry a runtime payload and contribute services to the host via `Configure()`. **`content`** packages carry a payload but no entry point — the loader skips them; built-in commands resolve their install dirs by package id (e.g. the host runtime; spec §4.6). **`meta`** packages carry no payload — they bundle other workload-or-content packages via the `.nuspec`'s `<dependencies>` (§5.5 "Meta packages"). |
| `entryPoint.assemblyPath`   | Required for `workload`; **forbidden** for `content` and `meta` | Path to the workload assembly **relative to `tools/any/`** (forward slashes; no leading `/`; no `..` segments; not absolute). Conventionally a bare filename. The loader resolves it as `<workload-home>/workloads/<packageId>/<packageVersion>/tools/any/<assemblyPath>`. |
| `entryPoint.type`           | Required for `workload`; **forbidden** for `content` and `meta` | Fully-qualified name of the type extending the abstract `Workload` base class. The loader instantiates this type by name when the workload is activated. The pack target validates that this type exists in the assembly, derives from `Workload`, and has a public parameterless constructor (§9.1). |

That's the entire v1 schema. The manifest is intentionally minimal:

- **Display metadata** (title, description, aliases) lives in the
  `.nuspec` and is captured by the install pipeline from there.
  Display name and description for the running CLI come from the
  loaded `Workload` instance's `DisplayName` / `Description`
  properties (spec §5.3) — no duplication.
- **Compatibility constraints** (CLI version, contract-assembly
  versions) are deferred to §11.1.
- **Transitive workload-on-workload dependencies are not supported
  in v1** for `workload` or `content` packages. They are
  self-contained: every library each one needs is vendored into its
  `tools/any/` payload by `dotnet publish`. The only mechanism for
  packaging multiple workloads under a single id is a `meta`
  package whose `.nuspec` `<dependencies>` enumerates them (§5.5).
- **Capability lists / contribution-point inventories** are
  intentionally absent. Per spec §5.1 the contract is "what services
  the workload registers in `Configure`", not a static manifest;
  baking it into `workload.json` would just create a second source
  of truth that has to stay in sync with the code.

#### Forward compatibility

The CLI rejects any `workload.json` whose `$schema` URL it doesn't
recognise. A v1 CLI will not load a v2 manifest even if v2 only adds
fields that could be ignored. This is a deliberate trade-off:
**forward compatibility is sacrificed for safety**. Partially
interpreting a future schema risks loading a workload whose contract
the CLI doesn't fully understand (e.g., a v2 field marks the
workload as requiring a CLI capability that's missing). Strict
rejection forces an explicit opt-in (`func upgrade` or installing a
newer CLI) before such workloads are honoured.

#### Why a manifest instead of attribute scanning

The spec's original v1 had the install pipeline scan the package's
top-level assemblies for `[assembly: ExportCliWorkload<T>]`. We move
to an explicit manifest because:

1. **No code execution at install.** Reading JSON beats spinning up
   an `AssemblyLoadContext` (even a metadata-only one) just to discover
   an attribute.
2. **Faster install + better diagnostics.** Malformed packages fail
   with a clear "manifest missing/invalid" error before any reflection
   path runs.
3. **Inspectability.** Anyone can crack the `.nupkg` open in a zip
   tool and see what would be loaded. No .NET reflection knowledge
   required to audit a workload.
4. **Future non-.NET workloads.** The marker attribute is a .NET-only
   construct. A JSON manifest leaves the door open without committing
   to it in v1.

The marker attribute (`[assembly: ExportCliWorkload<T>]`) survives as
an **optional compile-time check** (§9.1): when present, the pack
target validates that `workload.json`'s `entryPoint.type` matches the
attribute's `T`. Authors can choose to drop the attribute entirely if
they prefer.

### 5.5 The three kinds, side-by-side installs, and meta expansion

Most of the install/uninstall/load lifecycle is owned by
[`workload-spec.md`](./workload-spec.md) §6.1, §6.2, §6.4, §6.5,
§6.7. This section captures the **layout-level** consequences of
that lifecycle plus the **meta-package install/uninstall flow**,
which the spec doc does not cover.

#### The three kinds at a glance

| Kind | `entryPoint`? | `tools/any/`? | `<dependencies>`? | Loader behaviour | Has install dir? |
|---|---|---|---|---|---|
| `workload` (default) | required | required | empty | Loaded into a collectible ALC; `Workload.Configure(builder)` called; contributes services | yes |
| `content` | forbidden | required | empty | Skipped — no ALC, no `Configure` call | yes |
| `meta` | forbidden | forbidden | non-empty | Skipped — never seen by the loader | **no** (registry row only) |

`workload` and `content` packages are full installable units:
extracted to their own per-version directory, recorded in the
registry's `workloads[]` array. `meta` packages exist purely as a
registry concept — installing one resolves and installs each member
as a `workload` or `content`, then records the resolved member list
in the registry's `metas[]` array; the meta itself never gets an
install dir.

#### Workload and content packages are self-contained

Every `workload` or `content` package's `.nuspec` `<dependencies>`
element is **empty**. Every library a `workload` needs at runtime is
vendored into its `tools/any/` payload by `dotnet publish`; every
file a `content` package ships is laid out under `tools/any/` by the
package author. Pack-time enforcement (§9.3) hard-fails any
`<PackageReference>` not marked `<PrivateAssets>all</PrivateAssets>`.

This matches `dotnet tool install` and `dotnet new install`, which
both ignore `<dependencies>` entirely (§3). It also keeps the
single-package install pipeline trivial: download, validate,
extract, record. No transitive resolution, no closure tracking, no
provenance metadata, no version-range arithmetic.

The only place `<dependencies>` is non-empty — and load-bearing for
the install pipeline — is `kind: meta` (below).

#### Side-by-side installs (SxS)

Multiple versions of the same `workload` or `content` `packageId`
may coexist on disk. Each install gets its own directory, keyed by
lowercased `packageId` (NuGet's normalization) and ordinal-matched
`packageVersion`:

```
<workload-home>/workloads/
  <packageid>/
    <version-A>/
      workload.json
      tools/any/
        <Workload>.dll
        ...
    <version-B>/
      workload.json
      tools/any/
        <Workload>.dll
        ...
```

The global registry (spec §10.1) records each install as a separate
`(packageId, packageVersion)` row in its `workloads[]` array. SxS is
triggered by `func workload install --force` (or by the user
answering "yes" to the SxS prompt) when a different version is
already installed (spec §6.1 step 1).

##### Activation policy: latest installed wins

The loader (spec §6.2 step 2) picks **one** version per `packageId`
per CLI invocation: the highest installed semver. Older installed
versions sit on disk but are not loaded into the running process.
They exist on disk so:

- `func workload list --all-versions` can show them.
- `func workload uninstall <id> <version>` can revert by removing
  the newer install — the loader then picks the next-highest
  remaining version on the next CLI invocation.
- `func workload prune` (spec §6.7) can clean them up later.

There is **no project-level pinning, no `activeVersions` pointer,
no per-profile selection** in v1. "Latest installed wins" is the
entire policy (spec §6.2 "Multiple-version policy").

##### Metas are unique per id

The `metas[]` array carries at most one row per meta `packageId`.
There is no SxS for metas. Reasons:

- Metas don't run anything — there's no "live version" to pick.
- Their only purpose is to record "user asked for bundle X" so
  uninstall can offer cascade. Two rows would make
  `func workload uninstall <metaId>` ambiguous.
- Member SxS is already first-class at the workload/content level;
  the meta layer doesn't need its own SxS to enable that.

Installing a different version of an already-installed meta is an
**update** (re-resolve members from the new version's
`<dependencies>`, install new members, replace the `metas[]` row in
place). Members that the new meta no longer references are left on
disk — they're independently useful workloads now and the user can
remove them explicitly. See "Meta-package update" below.

#### Meta packages

A meta package bundles a curated set of workloads and/or content
packages under a single id. Installing a meta installs each member
as a normal `workload` or `content`. The meta itself contributes no
runtime — it is purely an install-time artifact.

##### Why the discriminator (`kind`) lives in `workload.json`

The `.nuspec` `<packageType>` value is `FuncCliWorkload` for all
three kinds. NuGet V3 `SearchQueryService` accepts only one
`packageType` filter per query, so using one type keeps feed search
to a single round-trip. The `kind`-vs-kind discrimination then
happens by reading the in-package `workload.json` after download.

Trade-off: `kind` is not visible in NuGet feed metadata, so a
pack-time check that "all of this meta's members are
`kind: workload` or `kind: content` (i.e., not other metas)"
requires opening each member `.nupkg` to read its `workload.json`.
The pack target (§9.3) opts to defer that check to **install time**
in v1, where the error message is clear and the failure surfaces in
the author's own dev loop (they install their own meta to test it).

Mitigations the layout *does* take advantage of, both feed-visible
and free:

- **`kind:*` tags** in the `.nuspec`. Authors **should** set one of
  `kind:workload` / `kind:content` / `kind:meta` in `<tags>`. Feed
  UIs and `func workload search --kind <kind>` use this for cheap
  filtering. The tag is convention only — `workload.json` remains
  authoritative — but it covers the common search path without
  per-package downloads.

##### Meta install flow

`func workload install <metaId>` runs spec §6.1's eight-step flow
once for the meta itself, then expands its members:

1. **Resolve and download the meta package.** Standard
   `(packageId, packageVersion)` resolution against the configured
   feeds. Validate the package type is `FuncCliWorkload`, open it,
   read `workload.json`, and confirm `kind: meta`.
2. **Read the `.nuspec` `<dependencies>`.** Reject if empty (an
   empty meta is a packaging error caught at pack time, but
   defended in depth at install time).
3. **Pre-flight: resolve and download every member** before any
   extraction. For each `<dependency>` entry: select the
   highest-feed-version satisfying the declared range,
   `FindPackageByIdResource`-download to a scratch staging
   directory, and read each member's `workload.json` to assert
   `kind ∈ {workload, content}` (rejecting nested metas). Pre-flight
   surfaces resolution errors, range mismatches, and meta-of-meta
   violations *before* any byte hits the install location, so the
   common failure modes don't strand the user mid-bundle.
4. **Extract each member.** For each pre-flighted member,
   atomic-rename its staged `tools/any/` into
   `<workload-home>/workloads/<packageId>/<packageVersion>/`. This
   step is fast (rename only — bytes are already on disk in the
   staging dir from pre-flight).
5. **Record each member's registry row** in `workloads[]`. New rows
   carry `installedExplicitly: false` (members brought in by the
   meta, not by an explicit `func workload install <id>`). If a
   row for the same `(packageId, packageVersion)` already exists,
   skip; if it exists with `installedExplicitly: true`, the row's
   `installedExplicitly` stays `true` (the user installed it
   explicitly *and* it's now in a meta — see "uninstall flow"
   below).
6. **Record the meta itself last** in `metas[]`. The row carries
   `(metaPackageId, metaPackageVersion)` and a frozen `members[]`
   snapshot of the resolved `(packageId, packageVersion)` pairs.
   Writing `metas[]` last means a crash mid-install never leaves a
   half-finished meta row — the user runs `func workload list`
   and sees the partially-installed members as standalone, with
   no broken meta state to recover from.

The `.nupkg` of the meta itself is **discarded** after step 6 (and
after each member's extraction). v1 does not cache it. If a future
`func workload repair` needs to re-resolve a meta's members, it
re-downloads the `(metaPackageId, metaPackageVersion)` from the
configured feeds.

If `func workload install <metaId>` is run when the meta is already
recorded in `metas[]`, the request becomes a **meta-package update**
— see below.

##### Meta-package update

`func workload install <metaId> --force` (or accepting the
"different version detected" prompt) on an installed meta:

1. Resolve and download the new meta version; pre-flight all of its
   members (same as step 3 of the install flow).
2. For each pre-flighted member not already in `workloads[]`,
   extract and record (`installedExplicitly: false`).
3. For each member already in `workloads[]`, leave it alone —
   idempotency. The `installedExplicitly` flag is unchanged.
4. **Replace** the meta's `metas[]` row in place with the new
   version + new `members[]` snapshot. The old version's frozen
   `members[]` is gone.
5. Members that the new meta version no longer references are
   **left on disk and in `workloads[]`** unchanged. The user can
   remove them explicitly with `func workload uninstall <id>` if
   they want.

Update is non-destructive of pre-existing members. The meta layer
only ever adds rows or rewrites its own row.

##### Meta uninstall flow

`func workload uninstall <metaId>` runs:

1. Look up the meta's `metas[]` row. Snapshot its `members[]` list.
2. **Default (interactive):** print
   ```
   Uninstalling meta package WorkerFamily 1.0.0.
   It was installed with the following members:
     [x] Workload.Node     2.0.0  (also installed explicitly — keep checked to remove)
     [x] Workload.Python   2.0.0
     [ ] Workload.Java     2.0.0  (installed explicitly; default: keep)
   Remove selected members?
   ```
   Pre-checking is driven by each member's `installedExplicitly`
   flag: members where the flag is `false` are pre-checked for
   removal; members where it is `true` are pre-checked **off**
   (they were installed explicitly and the user probably wants to
   keep them). The user adjusts the selection and confirms.
3. `--meta-only`: skip the prompt; only remove the `metas[]` row.
   Members are untouched.
4. `--with-dependencies`: skip the prompt; remove every member in
   the snapshot **whose `installedExplicitly` flag is `false`**.
   Members with `installedExplicitly: true` are kept (the user
   installed them independently of this meta; the meta uninstall
   should not clobber them). To force-remove explicitly-installed
   members too, pass `--with-dependencies --force`.
5. `--with-dependencies --force`: remove every member listed in
   the meta's `members[]` snapshot, regardless of
   `installedExplicitly`. This is the "blow away the bundle's
   footprint" escape hatch.
6. Member uninstall uses **id-only lookup** (fuzzy): if the
   snapshot's `(memberId, memberVersion)` no longer matches a row
   in `workloads[]` (e.g., the user ran `func workload update
   <memberId>` since the meta was installed and the version in the
   snapshot is gone), the cascade still finds the current row by
   id and removes it. This matches the `--with-dependencies`
   intent ("the bundle's footprint"). Strict-version lookup is
   recorded as an open question (§11) for future revisiting.
7. Once all selected members are removed, remove the `metas[]`
   row.

A member listed in two metas (rare) is removed by the first
`--with-dependencies` cascade that hits it; the second meta's
`metas[]` row then references a missing member. `func workload
list` surfaces this as a `[broken meta]` warning; remediation is
deferred to a future `func workload repair` (§11).

##### `installedExplicitly` lifecycle

The flag is per-row in `workloads[]`. Transitions:

- `func workload install <id>` → row created with
  `installedExplicitly: true`.
- Meta install brings in a new member → row created with
  `installedExplicitly: false`.
- `func workload install <id>` on a row that already exists with
  `installedExplicitly: false` → flag flips to `true`. (The user
  is now claiming it.)
- The flag never flips back to `false`. Once explicit, always
  explicit, until uninstalled.

This is a deliberate simplification of v0.7's `installedBy`
reason-list. We don't need to track *which* metas brought a member
in — only "did the user ask for it directly at any point." The
member's frozen `members[]` snapshot in each meta covers the rest.

#### What we use NuGet for, what we own

We use NuGet for:

- Feed protocol (V2 + V3), package download — `NuGet.Protocol`.
  `FindPackageByIdResource` for download (the same API
  `dotnet tool install` uses); `PackageMetadataResource` for
  per-package metadata (id, version, package types, tags,
  `<dependencies>`).
- Version range syntax (`[1.2.0,2.0.0)`) and comparison —
  `NuGet.Versioning`. Used for meta-member range resolution and
  installed-vs-requested SxS decisions.
- `nuget.config` discovery, source ordering, package source
  mapping, credential providers — `NuGet.Configuration`,
  `NuGet.Credentials`.
- `.nupkg` / `.nuspec` parsing — `NuGet.Packaging`.

We **don't** use:

- `NuGet.Resolver` / `NuGet.PackageManagement` —
  `RestoreCommand` / `RestoreRunner`. Workload packages are not
  projects; per-workload resolution at install would frame them as
  references with asset selection rules that don't apply to
  payload-vendored packages. See §10.5.
- `NuGet.DependencyResolver.Core`. Meta resolution is one level
  deep (no meta-of-meta in v1; member ranges resolve independently
  against the feed); transitive graph walking would be unused
  machinery.

We own:

- The install layout and per-version install dirs (see §5.1, §5.3).
- `kind` enforcement — pack-time checks for `workload` /
  `content`, install-time checks for `meta` member kinds.
- Meta expansion — pre-flight + per-member extraction + member
  recording + meta row write.
- Activation under SxS — loader picks one version per id per
  invocation.
- Uninstall semantics — interactive prompt + `--meta-only` /
  `--with-dependencies` / `--force` flags; `installedExplicitly`
  bookkeeping.
- Atomic registry writes — temp file + rename; concurrent-safe.
- Recovery — broken-meta detection in `list` (§11).

#### What the layout does not need to record

The full registry shape (per-row fields, `$schema` URL versioning,
the `aliases`/`source` columns, the `metas[].members[]` snapshot,
the `installedExplicitly` flag) is owned by spec §10.1. This doc
commits only to the on-disk install layout that makes that registry
shape possible:

- A per-version install directory under the workloads home keyed
  by `(packageId, packageVersion)` for each `workload` or `content`
  package.
- `workload.json` at the install-dir root, copied verbatim from the
  package, so the loader can re-read it without going back to the
  package.
- `tools/any/` next to it, containing whatever the package shipped.
- **No install dir for `meta` packages.** The meta is recorded only
  in the central registry's `metas[]` array.
- No CLI-managed metadata files inside any install dir beyond what
  the package itself shipped — install metadata
  (timestamp, source feed, alias list, `installedExplicitly` flag,
  `members[]` snapshot) lives in the central registry.

#### Recovery cases at the layout level

- **Orphan install dir** (move-into-place succeeded, registry
  write failed): there's an extracted `(packageId, packageVersion)`
  directory the CLI doesn't know about. `func workload prune`
  (spec §6.7) cleans these up.
- **Missing install dir** (registry has a row whose install dir is
  missing): the loader surfaces this as a `[<packageId>]`-prefixed
  warning and continues (spec §6.2 "Failure isolation"). A future
  `func workload repair` re-extracts from a fresh download.
- **Broken meta** (a `metas[].members[]` entry references a
  `(packageId, packageVersion)` not present in `workloads[]`):
  `func workload list` shows `[broken meta]` next to the meta's
  row. Remediation deferred to `func workload repair` (§11).
- **Crashed meta install** (some members extracted, no `metas[]`
  row): the partially-installed members appear in `workloads[]`
  with `installedExplicitly: false`. The user re-runs
  `func workload install <metaId>`; idempotency skips already-
  installed members, fills in any missing ones, and writes the
  `metas[]` row. The previously-failed member rows are absorbed
  into the meta's `members[]` snapshot — they were intended as
  members all along. (Documented design point.)

The atomic-rename used by the install pipeline plus the registry's
atomic write keep these failure modes mutually exclusive at the
bytes level.

## 6. Host/workload compatibility

A workload is binary-compatible with a CLI when **both** of these hold:

1. **Host-shared contract assemblies:** the workload was built against
   contract assemblies whose public surface the running CLI still
   satisfies. The contract assemblies are the ones the loader
   delegates back to the default load context — currently
   `Azure.Functions.Cli.Abstractions`,
   `Microsoft.Extensions.DependencyInjection.Abstractions`, and
   `System.CommandLine`
   ([`src/Func/Workloads/Loading/WorkloadLoadContext.cs:73-76`](../src/Func/Workloads/Loading/WorkloadLoadContext.cs)).
   `System.CommandLine` is on the list today only as a transitional
   measure; it is expected to be removed from the shared contract
   before v1, leaving `Abstractions` and the DI abstractions as the
   stable surface. The CLI ships exactly one version of each; the
   workload's resolved reference must bind to that version. The
   workload's TFM is recorded in its assembly's
   `TargetFrameworkAttribute` and `.deps.json`; the .NET runtime's
   standard load rules handle TFM compatibility (a `net10.0`
   workload runs on a CLI on a `net10.0`-or-newer runtime without
   further intervention).
2. **Shared frameworks:** the workload may **not** depend on any
   shared framework (`Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`,
   `Microsoft.WindowsDesktop.App`) the CLI does not itself carry.
   Workloads do not ship their own `.runtimeconfig.json` — they run
   inside the CLI's process and inherit its framework set. A workload
   that needs ASP.NET shared framework primitives must vendor them or
   target a CLI that already loads them.

The package layout cannot fully express (1) on its own. Two
candidate mechanisms for the workload to declare its compatibility
window — neither is committed in v1, both are open questions:

- **NuGet `<dependencies>`** on the contract packages
  (`Azure.Functions.Cli.Abstractions`, etc.) with version ranges. The
  install pipeline reads these and rejects packages whose ranges
  exclude the running CLI. This is the closest match to existing
  NuGet conventions but requires the install pipeline to evaluate
  ranges.
- **A first-class CLI-version field** in the package's metadata
  (e.g. via a reserved tag like `func-cli-min:5.2`). Simpler to
  evaluate; requires a Func-defined convention.

See §11 for the open question.

> **Authoring rule (consequence of (1)):** the contract assemblies
> **must not** be packed inside the workload — see §9.2.

## 7. Required NuGet metadata

These already appear in `workload-spec.md` §5.3; restated here so the
layout is self-contained. The in-package `workload.json` (§5.4) is
**required** in addition to this metadata — it lives outside the
`.nuspec` because it carries CLI-specific contract data the
`.nuspec` schema can't express cleanly. The role of the `.nuspec`'s
`<dependencies>` element **branches on `kind`** (§5.5):

- `kind: workload` and `kind: content`: `<dependencies>` is
  **empty**. Library `<PackageReference>`s in the package's
  `.csproj` must carry `<PrivateAssets>all</PrivateAssets>` per
  §9.2 and §9.3 so `dotnet publish` vendors their runtime into
  `tools/any/` rather than declaring it as a NuGet dependency.
- `kind: meta`: `<dependencies>` is **non-empty** and lists each
  bundled `kind: workload` or `kind: content` member. The install
  pipeline reads it to drive meta-expansion (§5.5). Each
  `<dependency>` must resolve to a `FuncCliWorkload`-typed package
  on the configured feeds; the deeper `kind` check on members
  (must be `workload` or `content`, not another meta) happens at
  install time.

| Field          | Value                                                 |
|----------------|-------------------------------------------------------|
| `id`           | `Azure.Functions.Cli.Workload.<Name>` for first-party workloads. Third-party authors choose any NuGet-legal id. |
| `version`      | SemVer 2.0.                                           |
| `title`        | Display name shown in `func workload list`.           |
| `description`  | One-line summary shown in `func workload list`.       |
| `packageTypes` | **Must** include a `FuncCliWorkload` entry. Packages without this type are rejected by `func workload install`. |
| `tags`         | **Should** include exactly one `kind:<workload\|content\|meta>` tag matching `workload.json`'s `kind` field (§5.5; convention only — `workload.json` remains authoritative). **Should** also include one or more `alias:<name>` tags so users can install by short name. The `func-workload` tag is **recommended** (improves discoverability in NuGet feed UIs and `nuget search`-style tooling that does not filter by package type) but **not required** — the `FuncCliWorkload` package type already self-identifies the package, and `func workload search` filters by that plus optionally by `kind:`. |
| `dependencies` | **Branches on `workload.json`'s `kind` field** (§5.4): for `kind: workload` and `kind: content`, **must be empty** — every `<PackageReference>` must carry `<PrivateAssets>all</PrivateAssets>` so its runtime is vendored into `tools/any/`. For `kind: meta`, **must be non-empty** and list each bundled member as a `<dependency>` resolving to a `FuncCliWorkload`-typed package on the configured feeds (the pack target validates the package type cheaply via `PackageMetadataResource`; the deeper `kind ∈ {workload, content}` check on members is deferred to install time). See §5.5, §9.3. |
| `readme`       | Path to `README.md` packed at the root.               |
| `icon`         | Path to `icon.png` packed at the root.                |
| `releaseNotes` | Recommended. Sourced from `release_notes.md` via the existing `Release.targets`. |

## 8. Worked examples

### 8.1 Normal workload: `Azure.Functions.Cli.Workload.Node`

```
Azure.Functions.Cli.Workload.Node.1.0.0.nupkg
├── tools/
│   └── any/
│       ├── Azure.Functions.Cli.Workload.Node.dll
│       ├── Azure.Functions.Cli.Workload.Node.deps.json
│       ├── Azure.Functions.Cli.Workload.Node.pdb
│       └── Newtonsoft.Json.dll                       # transitive managed dep
├── workload.json
├── README.md
├── icon.png
└── Azure.Functions.Cli.Workload.Node.nuspec
```

> Host-shared contract assemblies (`Azure.Functions.Cli.Abstractions.dll`,
> `Microsoft.Extensions.DependencyInjection.Abstractions.dll`, and —
> transitionally — `System.CommandLine.dll`) are **not** in the
> package — see §6 (2) and §9.2.
>
> Templates for `func init` / `func new` are **not** in the worked
> example: whether they ship inside the workload package or as a
> separate `Template`-typed package is open (§11). Authors who do
> ship templates in-package would place them under
> `tools/any/<dir>/` alongside the workload assembly.

`workload.json` (`kind` field omitted; `workload` is the default):

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "entryPoint": {
    "assemblyPath": "Azure.Functions.Cli.Workload.Node.dll",
    "type": "Azure.Functions.Cli.Workload.Node.NodeWorkload"
  }
}
```

Note `assemblyPath` is a **bare filename** (relative to `tools/any/`),
not a path including the `tools/any/` prefix; the loader prepends the
content-root directory itself (§5.4).

`.nuspec` excerpt:

```xml
<metadata>
  <id>Azure.Functions.Cli.Workload.Node</id>
  <version>1.0.0</version>
  <title>Node.js</title>
  <description>func init / func new support for Node.js and TypeScript.</description>
  <authors>Microsoft</authors>
  <packageTypes>
    <packageType name="FuncCliWorkload" />
  </packageTypes>
  <tags>kind:workload alias:node alias:javascript alias:typescript</tags>
  <readme>README.md</readme>
  <icon>icon.png</icon>
  <!-- No <dependencies>: workload packages are self-contained (§5.5, §9.3). -->
</metadata>
```

### 8.2 Content package: Functions host runtime

The Func host (`Microsoft.Azure.WebJobs.Script.WebHost`) ships as a
`kind: content` package (§5.5; spec §4.6). The package contributes
no `Workload` class and no DI services; `func start` resolves the
install directory by package id and launches the host binaries
directly.

```
Azure.Functions.Cli.Workload.Host.4.0.0.nupkg
├── tools/
│   └── any/
│       ├── Microsoft.Azure.WebJobs.Script.WebHost.dll
│       ├── Microsoft.Azure.WebJobs.Script.WebHost.deps.json
│       ├── runtimes/
│       │   ├── win-x64/...
│       │   ├── linux-x64/...
│       │   └── osx-x64/...
│       └── ...                                       # rest of host's published output
├── workload.json
├── README.md
└── Azure.Functions.Cli.Workload.Host.nuspec
```

`workload.json`:

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "content"
}
```

`.nuspec` excerpt:

```xml
<metadata>
  <id>Azure.Functions.Cli.Workload.Host</id>
  <version>4.0.0</version>
  <title>Functions host runtime</title>
  <description>The Microsoft.Azure.WebJobs.Script host process that func start launches.</description>
  <authors>Microsoft</authors>
  <packageTypes>
    <packageType name="FuncCliWorkload" />
  </packageTypes>
  <tags>kind:content alias:host</tags>
  <readme>README.md</readme>
  <!-- No <dependencies>: content packages are self-contained (§5.5, §9.3). -->
</metadata>
```

The loader (spec §6.2 step 3) skips this entry — no
`AssemblyLoadContext` spin-up, no `Workload.Configure` call, no DI
contribution. `func start` queries the workload-registry service
for the `WorkloadEntry` instances it exposes, locates the host
workload purely by its package id
(`Azure.Functions.Cli.Workload.Host`), picks the right version
— either the latest installed or the version that satisfies the
active profile's requirement — and launches the host binary by
path out of
`<workload-home>/workloads/azure.functions.cli.workload.host/<resolved-version>/tools/any/`.
This resolution is owned by `func start` and is fully isolated
from the core CLI loading logic; the loader never sees this
package, and `func start` never re-reads `workload.json` or the
registry file directly — it goes through the service.

### 8.3 Meta package: `Azure.Functions.Cli.Meta.WorkerFamily`

A fictitious "Worker Family" meta bundles Node, Python, and Java
under a single versioned id (§5.5 "Meta packages"); we don't plan
to ship this package — it's used here purely to illustrate the
shape:

```
Azure.Functions.Cli.Meta.WorkerFamily.1.0.0.nupkg
├── workload.json                                     # kind: meta — no entryPoint
├── README.md
└── Azure.Functions.Cli.Meta.WorkerFamily.nuspec
                                                     # No tools/any/ — metas have no payload.
```

`workload.json`:

```json
{
  "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
  "kind": "meta"
}
```

`.nuspec` excerpt:

```xml
<metadata>
  <id>Azure.Functions.Cli.Meta.WorkerFamily</id>
  <version>1.0.0</version>
  <title>Worker Family</title>
  <description>Curated bundle of Func workloads for Worker-family languages.</description>
  <packageTypes>
    <packageType name="FuncCliWorkload" />
  </packageTypes>
  <tags>kind:meta</tags>
  <readme>README.md</readme>
  <dependencies>
    <group>
      <dependency id="Azure.Functions.Cli.Workload.Node"   version="[2.0.0,3.0.0)" />
      <dependency id="Azure.Functions.Cli.Workload.Python" version="[2.0.0,3.0.0)" />
      <dependency id="Azure.Functions.Cli.Workload.Java"   version="[2.0.0,3.0.0)" />
    </group>
  </dependencies>
</metadata>
```

The meta's `.csproj` declares each member as a plain
`<PackageReference>` — **no** `<PrivateAssets>` (which would hide the
reference from `<dependencies>`):

```xml
<ItemGroup>
  <PackageReference Include="Azure.Functions.Cli.Workload.Node"   Version="[2.0.0,3.0.0)" />
  <PackageReference Include="Azure.Functions.Cli.Workload.Python" Version="[2.0.0,3.0.0)" />
  <PackageReference Include="Azure.Functions.Cli.Workload.Java"   Version="[2.0.0,3.0.0)" />
</ItemGroup>
```

`func workload install Azure.Functions.Cli.Meta.WorkerFamily`
pre-flights all three members, resolves each to a concrete
`(packageId, packageVersion)`, downloads them into a staging area,
extracts each into its own
`<workload-home>/workloads/<packageId>/<packageVersion>/` directory
(side-by-side with any pre-existing installs of the same id; SxS
applies normally), records each member in `workloads[]` with
`installedExplicitly: false`, and finally records the meta itself
in `metas[]` with the resolved `members[]` snapshot. See §5.5
"Meta install flow".

## 9. Authoring impact (preview — full plumbing in a follow-up)

Producing this layout from a workload csproj will be a small set of
properties on top of `dotnet pack`'s defaults. The full
`Workload.Common.targets` / `Workload.props` plumbing is out of scope
for this design, but the salient knobs will be roughly:

> **Workload authoring SDK.** We plan to ship a dedicated **Func
> Workload SDK** that workload authors reference instead of the
> standalone targets — at a minimum a `Azure.Functions.Cli.Workload.Sdk`
> package (working name) that an author imports via
> `<Project Sdk="Azure.Functions.Cli.Workload.Sdk/x.y.z">`
> and that bundles the pack target, the `workload.json` generator,
> the host-shared-assembly exclusions (§9.2), and the pack-time
> rules (§9.3). The properties shown below are the knobs the SDK
> will expose; everything else (publish-into-`tools/any/`, manifest
> generation, contract-assembly stripping, package-type wiring)
> the SDK takes care of. The SDK is out of scope for this layout
> doc — it's tracked separately and will live alongside this
> design — but it's worth flagging here so authors don't conclude
> they'll need to hand-wire each property and target into every
> workload `.csproj` forever.

```xml
<PropertyGroup>
  <PackageType>FuncCliWorkload</PackageType>
  <PackageTags>func-workload alias:node</PackageTags>
  <IncludeBuildOutput>false</IncludeBuildOutput>  <!-- we use publish output, not build output -->
  <IsPackable>true</IsPackable>
</PropertyGroup>
```

Plus a target that runs `dotnet publish`, copies the publish output
into `tools/any/`, and **generates `workload.json`**. We **cannot**
reuse the SDK's `PackAsTool=true` machinery: that target (imported
from `Microsoft.NET.PackTool.targets` only when `PackAsTool=true`) is
hard-coded to set `<PackageType>DotnetTool</PackageType>` and emit a
`DotnetToolSettings.xml`, both of which are wrong for a workload. The
follow-up plumbing PR ships a parallel pack target (working name
`Workload.Common.targets`) that re-implements the publish-into-tools
step without the `DotnetTool` semantics.

### 9.1 Generating `workload.json`

The pack target produces `workload.json` automatically; authors
should not hand-author it. The pack inputs are, in priority order:

1. **MSBuild properties** the author sets on the project — e.g.
   `<FuncWorkloadKind>workload|content|meta</FuncWorkloadKind>`
   (default `workload`) and, for `kind: workload`,
   `<FuncWorkloadEntryPointType>` for the entry-point FQN. When
   `kind` is `content` or `meta`, no entry-point inputs are needed;
   otherwise `assemblyPath` is inferred from the project's
   published output filename (relative to `tools/any/`,
   conventionally a bare filename).
2. **The `[assembly: ExportCliWorkload<T>]` marker attribute** on the
   workload assembly, when present (only applicable for
   `kind: workload`). The pack target reads `T`'s FQN from the
   published assembly's metadata (no execution required — this is
   metadata-only inspection during pack, not at install).

If both are present, the pack target validates they agree and fails
the pack if they don't. If neither is present and `kind` is
`workload`, the pack fails: the author must declare the entry point
explicitly. For `kind: content` and `kind: meta` packages, no
entry-point inputs are consulted.

The marker attribute remains useful as a **compile-time** check of
`T : Workload` — the constraint is enforced by the C# compiler the
moment `[ExportCliWorkload<MyWorkload>]` is written. Authors who
prefer a property-only flow can drop the attribute entirely.

### 9.2 Excluding host-shared contract assemblies

The CLI's load context delegates a fixed set of contract assemblies
back to the default load context so types crossing the host/workload
boundary keep a single `Type` identity
([`WorkloadLoadContext.cs:73-76`](../src/Func/Workloads/Loading/WorkloadLoadContext.cs)).
Today the set is:

- `Azure.Functions.Cli.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `System.CommandLine` *(transitional — slated to be removed from
  the shared contract before v1; workloads will then take a direct
  package reference to whichever version they need and the loader
  will load it inside the workload's ALC like any other library)*

If the workload package ships its own copy of any of these, three
things go wrong: the package gets bigger for no reason, the workload
carries a stale copy that drifts out of sync with the running CLI, and
the loader's "default-context wins" rule has to deduplicate at every
load. The fix is to keep these out of the workload's runtime closure
in the first place. For each contract reference the workload csproj
should set:

```xml
<PackageReference Include="Azure.Functions.Cli.Abstractions">
  <PrivateAssets>all</PrivateAssets>
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

The pack target should additionally **validate** that none of the
host-shared contract assemblies appear inside `tools/any/` before
producing the `.nupkg`. This is the same validation rule for all
three assemblies, not an `Abstractions`-special-case.

> The exact list of host-shared contract assemblies is owned by the
> CLI, not the workload. The pack target should read it from a
> well-known property the Abstractions package contributes (e.g.
> `@(FuncCliHostSharedContractAssembly)`) so the list stays in lockstep
> with `WorkloadLoadContext`.

### 9.3 Pack-time enforcement of dependency declarations

§5.5 makes the `.nuspec`'s `<dependencies>` element load-bearing for
the install pipeline, with rules that **branch on the package's
`kind`**. `Workload.Common.targets` enforces these rules at pack time.

#### For `kind: workload` packages

1. **Every `<PackageReference>` must carry
   `<PrivateAssets>all</PrivateAssets>`.** This suppresses the
   reference from the published `.nuspec` `<dependencies>` and lets
   `dotnet publish` vendor the library's runtime into `tools/any/`.
   Any `<PackageReference>` not so marked causes a hard pack failure
   with a fixit message: workload packages are self-contained
   (§5.5), so every reference is necessarily a private library
   reference. The check catches the "library leaked into
   `<dependencies>`" failure mode at pack time, before a malformed
   package is published.

2. **The published `.nuspec` `<dependencies>` element must be empty
   or absent.** Defense-in-depth check, run after rule (1).

3. **Manifest-shape validation** (`workload.json`):
   - `kind` is `"workload"` or absent (defaults to workload).
   - `entryPoint.assemblyPath` and `entryPoint.type` are present.
   - `entryPoint.assemblyPath` is not absolute, not rooted, and
     contains no `..` segments.

4. **`tools/any/` must be non-empty** and contain the workload
   assembly named in `entryPoint.assemblyPath`.

5. **Entry-point validation:**
   - `entryPoint.type` must exist inside that assembly's metadata
     (no execution required —
     Mono.Cecil / `System.Reflection.Metadata` scan).
   - The type must derive from the abstract `Workload` base class.
   - The type must have a public parameterless constructor.

6. **The `kind:workload` tag should be present** in `<tags>`.
   Recommendation, not a hard failure (the tag is convention only —
   `workload.json` remains authoritative). Missing-tag emits a
   warning fixit so the package shows up under `func workload
   search --kind workload` without a package download.

#### For `kind: content` packages

1. Rules (1) and (2) from `kind: workload` apply unchanged: empty
   `<dependencies>`, `<PrivateAssets>all</PrivateAssets>` on every
   `<PackageReference>`. Content packages are self-contained too;
   transitive resolution is the workload-pipeline path we don't run
   for either kind.

2. **Manifest-shape validation:**
   - `kind` is `"content"` (must be explicit — not the default).
   - `entryPoint` is **absent**. A `content` package with an
     `entryPoint` is rejected at pack time even though the install
     pipeline silently ignores it. Keeps `workload.json` a single
     source of truth for the package's role.

3. **`tools/any/` must be non-empty.** The pack target does not
   validate its internal layout — that's between the package and
   whatever built-in command consumes it (e.g., the host workload
   ships a host runtime; `func start` knows that layout).

4. **The `kind:content` tag should be present** in `<tags>`.
   Same severity as for `workload`: warning fixit.

#### For `kind: meta` packages

1. **The `.nuspec` `<dependencies>` element must be non-empty.** A
   meta with no members is a packaging error. Each `<dependency>`
   must declare a version range; bare ids are rejected.

2. **Every emitted `<dependency>` must resolve to a
   `FuncCliWorkload`-typed package on the configured feeds.** The
   pack target queries `PackageMetadataResource` for each member's
   metadata and verifies the `<packageType>`. Catches authors
   pointing a meta at non-workload packages.

3. **The deeper kind check is deferred to install time.**
   Pack-time only verifies the `FuncCliWorkload` package type on
   members; it does **not** verify each member's in-package
   `workload.json` `kind` field is `"workload"` or `"content"`.
   That check would force per-member `.nupkg` downloads at pack
   time. The trade-off is acceptable in v1 because:
   - Authors typically install their own meta locally to test it,
     hitting the install-time error in their own dev loop.
   - The install-time error message is clear ("meta member X is
     `kind: meta`; meta-of-meta is forbidden in v1, see §5.5").
   - An MSBuild task that downloads + reads each member's
     `workload.json` is recorded as a future enhancement (§11).

4. **`tools/any/` must be empty or absent.** Metas have no
   payload. A meta package with a non-empty `tools/any/` is
   rejected at pack time.

5. **Manifest-shape validation:**
   - `kind` is `"meta"` (explicit).
   - `entryPoint` is **absent**.

6. **The `kind:meta` tag should be present** in `<tags>`. Same
   severity as for `workload` / `content`: warning fixit.

#### Cross-cutting

The combination of rules (1)–(6) per kind, plus §9.2's contract-
assembly exclusion, means that for a successfully packed first-
party workload / content / meta, the install pipeline can trust
both `<dependencies>` and `workload.json` to be well-formed.
Failures move to CI where they're visible and actionable, instead
of being delivered to end users at install time.

## 10. Alternatives considered (and rejected)

### 10.1 `lib/<tfm>/` for the workload assembly

Already covered in §5.1. The decisive issue is that `lib/` packages are
indistinguishable from referenceable libraries to NuGet feeds and to
downstream tooling, which contradicts the `FuncCliWorkload` contract.

### 10.2 Custom top-level folder (`workload/`, `extension/`, …)

Tempting because it makes the package self-describing without any
NuGet folder semantics. Rejected because:

- It throws away every existing tool that already understands NuGet
  conventions (publish layouts, security scanners, feed UIs).
- It requires the install pipeline to know "the bytes live under
  `<custom-folder>/`," adding a contract that needs to evolve.
- The package-type + tag pair already self-describes the package; the
  folder layout is an implementation detail the install pipeline
  reads, not user-facing.

### 10.3 Workload-internal asset directories at the package root, outside `tools/`

Considered for any workload-internal data the workload reads at
runtime (e.g. embedded JSON schemas, or — if templates ship in-package
rather than as a separate `Template`-typed package, see §11 — the
template files): ship them under `/<assets>/` at the package root
rather than inside `tools/any/<assets>/`. Rejected because:

- It splits a single workload's assets across two unrelated package
  paths, which complicates the install pipeline (extract twice, track
  two roots).
- Putting assets next to the assembly mirrors `dotnet publish`
  output, so authors don't need any custom MSBuild plumbing.
- Workload code reads assets via paths relative to its assembly
  (`AppContext.BaseDirectory`), which is the natural pattern.

### 10.4 Scanning the package's assemblies for `[assembly: ExportCliWorkload<T>]`

The spec's original v1 (§6.1 step 4) had the install pipeline scan
the package's top-level assemblies for the marker attribute. Replaced
by `workload.json` (§5.4) in this iteration. Reasons:

- Requires loading (or at least metadata-only inspecting) assemblies
  during `func workload install`, which is more code and more failure
  modes than reading a JSON file.
- Diagnostics are worse: "no assembly with the marker attribute" or
  "multiple matches" surface only after the assemblies are inspected.
- Inspectability is worse: a user can't audit what would be loaded
  without .NET reflection knowledge.
- .NET-only by construction; closes the door on a future non-.NET
  workload mechanism.

The marker attribute itself is retained as an optional compile-time
check (§5.4, §9.1).

### 10.5 Use NuGet's `RestoreCommand` / `RestoreRunner` to drive install end-to-end

The most natural-looking alternative: model `func workload install`
as a project-restore operation. Synthesise a project file that
references the requested workload as a `<PackageReference>`, hand it
to `NuGet.Commands.RestoreCommand` (or `RestoreRunner`), and let
NuGet's full PackageReference-era pipeline — restore, conflict
resolution, lock files, audit, source mapping enforcement, asset
selection — produce a closed package graph that we then extract
into the workload install location.

Rejected because:

- **The .NET SDK's two NuGet-distributed CLI extensions explicitly
  don't do this.** `dotnet tool install` and `dotnet new install`
  both build their install pipelines on top of `NuGet.Protocol` +
  `NuGet.Versioning` + `NuGet.Packaging` + `NuGet.Configuration` /
  `NuGet.Credentials`. Neither references `RestoreCommand` /
  `RestoreRunner`; neither writes a `project.assets.json`. There
  was a vestigial `ProjectRestorer` that shelled out to
  `dotnet restore` ([`dotnet/sdk:src/Cli/dotnet/Commands/Tool/Install/ProjectRestorer.cs`](https://github.com/dotnet/sdk/blob/main/src/Cli/dotnet/Commands/Tool/Install/ProjectRestorer.cs)) — it is no longer
  instantiated in modern code paths. The SDK actively walked away
  from this approach.
- **A workload isn't a project.** It has no compile-time references,
  no asset selection per-TFM at install (the workload's runtime
  closure was selected when `dotnet publish` ran during pack), and
  no transitive runtime graph for the consumer to reconcile. Restore
  exists to close a *project's* graph; we already have a closed
  payload in `tools/any/`.
- **Asset-selection semantics are wrong.** `RestoreCommand` would
  try to pull `lib/<tfm>/` and `runtimes/<rid>/lib/<tfm>/` assets
  into a graph that the workload has no use for. Suppressing this
  means custom asset filters, which is more code than just walking
  `<dependencies>` ourselves.
- **No good fit for the install layout.** Restore writes its output
  to a project's `obj/` and a global packages folder. We want a
  per-workload, per-version subset extracted to a Func-managed
  location. Mapping one onto the other is friction, not free
  reuse.
- **Lock files / audit are not free either.** They are restore-time
  artifacts written into a project. Re-using them outside a project
  context is non-obvious; we would still own how lock files are
  surfaced in the CLI, where they live, and how they are validated
  on subsequent installs. If we want lock-file semantics later, we
  build them on top of our resolver pass — same as
  `dotnet tool install` would have to.
- **Confusing to authors.** A `FuncCliWorkload`-typed package
  showing up in some downstream consumer's `<PackageReference>` (as
  a side-effect of advertising restore semantics) would tempt
  library-style consumption, which doesn't make sense — the
  workload's runtime closure is already vendored and would pull
  duplicate copies of every transitive dep.

What this design *does* do: use `<dependencies>` as the declaration
vector for **`kind: meta` packages** (§5.5; `kind: workload` and
`kind: content` packages have empty `<dependencies>`), use
`NuGet.Protocol`'s `PackageMetadataResource` to read each declared
member's metadata from the feed, and use
`NuGet.Protocol`'s `FindPackageByIdResource` to download the
resolved set. Resolution is one level deep per install operation —
metas don't depend on metas in v1 (§5.5), so there is no
transitive graph to walk. That is *most* of what NuGet would have
done as part of restore — minus the project framing, the asset
selection, the project-output writing, and the transitive resolver
— and is exactly the slice `dotnet tool install` uses today
([`dotnet/sdk:src/Cli/dotnet/ToolPackage/ToolPackageDownloader.cs`](https://github.com/dotnet/sdk/blob/main/src/Cli/dotnet/ToolPackage/ToolPackageDownloader.cs)).

The remaining pieces — install layout, package-type and `kind`
enforcement, meta-expansion with pre-flighted member resolution,
side-by-side install dirs, "highest semver wins" activation,
interactive uninstall with `--meta-only` / `--with-dependencies`
flags and `installedExplicitly`-aware cascade, atomic registry
writes, concurrency — have no NuGet equivalent in any framing and
would have to be written either way. §5.5 and `workload-spec.md`
§6 lay them out.

## 11. Open questions (for the next iteration)

1. **Compatibility-window declaration** (§6) — does the workload
   declare its compatibility with the host via NuGet `<dependencies>`
   on the contract packages, via a Func-defined metadata field
   (e.g. a `func-cli-min:5.2` reserved tag), or as a new field in
   `workload.json` (§5.4)? With `<dependencies>` empty by §9.3 for
   `kind: workload` and `kind: content` packages, reusing it for
   contract-package version constraints would re-introduce the
   `<PackageReference>`-leak failure mode that rule exists to
   prevent. A `workload.json` field is the cleanest fit (richer
   constraint syntax, co-located with workload-specific metadata,
   no conflict with §9.3); a reserved tag is the cheapest to
   evaluate.

2. **Pack-time validation diagnostics** (§9.2, §9.3) — the
   per-`kind` rule sets in §9.3 (`<PackageReference>` suppression,
   manifest-shape validation, entry-point validation, member
   resolution for metas) need concrete diagnostic IDs, severities
   (warning vs. error), and fixit messages.

3. **Pack-time `kind: workload | content` member validation for
   metas** (§5.5, §9.3) — pack-time only verifies the
   `FuncCliWorkload` package type on a meta's members; the
   `kind ∈ {workload, content}` check is deferred to install time.
   A future MSBuild task could resolve each member to a cached
   `.nupkg`, read its in-package `workload.json`, and assert the
   member's `kind` at pack time. Trade-off: pack-time accuracy and
   earlier failure surface vs. pack-time cost (one network round
   trip per member if not cached) and added complexity in the pack
   target.

4. **Symbol packaging** — embed `.pdb` next to the `.dll`
   (`<DebugType>portable</DebugType>` +
   `<IncludeSymbols>false</IncludeSymbols>`) or ship a sidecar
   `.snupkg`? Embedding gives readable stack traces out of the
   box; sidecar shrinks the install footprint.

5. **Package signing & trust** (deferred from `workload-spec.md`
   §9 / §12 q2) — what does `func workload install` verify before
   activating a package? Author-signed only, repository-signed
   only, either, or neither for v1? Does `--source <local-path>`
   bypass signature checks (typical dev-loop expectation), and if
   so how is that surfaced in `func workload list`? Does the
   package layout need to reserve a slot for signature metadata,
   or is the standard NuGet `.signature.p7s` envelope sufficient?

6. **License files** — `MIT` is fine via
   `<PackageLicenseExpression>` for first-party workloads.
   Third-party authors will pack a `LICENSE` file; do we want to
   mandate either form?

7. **`func workload prune` / `repair` commands** — operational
   follow-on to §5.5's recovery cases. `prune` removes orphaned
   install dirs not referenced in the global registry (and is
   already speced as a real command in workload-spec §6.7);
   `repair` re-extracts workloads whose install dirs are missing
   or whose recorded hash mismatches what's on disk, **and**
   surfaces / heals broken metas (a `metas[].members[]` entry
   referencing a `(packageId, packageVersion)` not present in
   `workloads[]`). Both commands also need a defined behavior for
   metas: does `prune` remove a meta whose entire member set is
   missing? Does `repair` re-resolve missing members against the
   feed, given that the meta `.nupkg` itself was discarded after
   install (§5.5)? Out of scope for this layout doc but must be
   designed before v1.

8. **Lock files / reproducibility** — should the global registry
   be reproducible across machines via a `func workload restore`
   verb that replays a recorded install set? With SxS first-class
   (§5.5) and metas tracking their resolved `members[]`, the
   registry already carries enough to reconstruct
   `(packageId, packageVersion)` membership; the missing piece is
   a serialized, committable artifact (e.g.,
   `func.workloads.lock.json`) and the restore command itself.
   `dotnet tool install` doesn't have this today.

9. **NuGet audit / vulnerability scanning** — `NuGet.Protocol`
   does not surface vulnerability metadata as a side-effect of
   `PackageMetadataResource` queries. If `func workload install`
   should warn on known-vulnerable packages, we own that
   integration (likely against the GHSA-backed audit feed).
   Defer to v1.x.

10. **Templates: in-package or separate `Template`-typed package?**
    — A workload that contributes `func init` / `func new`
    templates can either embed them under `tools/any/<dir>/`
    (single package, single install) or publish them as a separate
    `<PackageType>Template</PackageType>` package consumed via the
    dotnet templating engine (independent versioning, reusable
    across workloads, but two installs and two-way version
    coordination). The trade-off depends on how `func` integrates
    with the templating engine — open until that integration is
    designed. The package-layout doc accommodates both: in-package
    templates sit under `tools/any/`; a separate package is just
    another NuGet artifact and is out of scope for this layout.

11. **Meta-of-meta relaxation** — v1 forbids a meta from depending
    on another meta (§5.5). When (if ever) do we relax this?
    Allowing meta-of-meta requires cycle detection on the meta
    graph, decisions about how nested members are recorded in
    `metas[].members[]` (snapshot the closure, or only direct
    members?), and the resolver implications of bundling
    transitive meta expansion. Worth deferring until a real
    curation use case pushes for it.

12. **Strict vs. fuzzy member-version lookup at meta uninstall**
    (§5.5 "Meta uninstall flow") — when
    `func workload uninstall <metaId> --with-dependencies` runs and
    a member's installed version on disk no longer matches the
    `members[]` snapshot (because the user updated the member
    independently), the v1 design uses **id-only (fuzzy)** lookup
    so the cascade still finds and removes the current row. The
    alternative (**strict**) is to skip with a warning when the
    snapshot's `(packageId, packageVersion)` is gone, preserving a
    deliberately-upgraded member. Both are defensible; revisit
    after v1 user feedback.

13. **Generalized content-only consumption** (spec §12.8) — built-
    in commands hard-code knowledge of the host workload's package
    id (`func start` knows the host's id and resolves its install
    directory). If a second `kind: content` workload appears, the
    CLI needs a non-hard-coded contribution mechanism (e.g.
    registering a content consumer with the loader) rather than
    continuing to enumerate package ids inline. Out of scope for
    this layout doc; the layout itself accommodates it.

---

*Once §11 is closed out, this doc will graduate from "proposed"
to "accepted" and move into `docs/` alongside
`building-a-workload.md`.*
