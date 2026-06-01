# Azure Functions CLI: .NET templates workload

This package ships function-scaffold templates for .NET Azure Functions
projects, consumed by the `func` CLI's `func new` command. It is a
**content workload** (Workload Spec §3; Templates Workload Spec §4.1):
the package carries metadata files only, with no entry-point assembly
and no runtime services. `func new` resolves the install directory by
package id and reads the catalog directly.

Unlike the Node and Python templates workloads, the .NET workload does
**not** carry per-template source files. The template content itself
lives in the upstream
`Microsoft.Azure.Functions.Worker.ItemTemplates` NuGet package; this
workload ships a fully-hydrated **catalog + parameter index**
(`dotnet-templates.json`) generated at workload build time from each
upstream `template.json`, plus a **pin** (`source.json`) recording the
exact upstream version (Templates Workload Spec §5.3, §6.3). At
`func workload install` time, the CLI provisions the pinned NuGet
package into the dotnet template hive; at `func new <id>` time the
CLI shells out to `dotnet new` against that offline-provisioned hive.

The workload pkg version is independent of the upstream
`Microsoft.Azure.Functions.Worker.ItemTemplates` version and follows
its own SemVer cadence. The DotNet workload has no extension-bundle
channel (Templates Workload Spec §4.4.3) — only the stable channel
ships in v1.

| Channel | Workload pkg version | Upstream NuGet pin                                |
|---------|----------------------|---------------------------------------------------|
| stable  | `1.0.0`              | `Microsoft.Azure.Functions.Worker.ItemTemplates`  |

The exact pinned upstream version is in `tools/any/content/source.json`
(written at pack time from `$(SourceItemTemplatesVersion)` in
`Directory.Version.props`) and is also stamped into
`dotnet-templates.json`'s `sourcePackage` field so consumers can
verify the catalog and the pin agree.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Templates.DotNet@1.0.0
# or by alias
func workload install dotnet-templates@1.0.0
```

Multiple templates versions coexist via `--force` (Workload Spec §4.6).

## Build-time pin selection

The upstream NuGet package id and version are driven by two MSBuild
properties in `Directory.Version.props`:

```bash
dotnet pack ... /p:SourceItemTemplatesVersion=4.0.5569
dotnet pack ... /p:SourceItemTemplatesPackageId=Microsoft.Azure.Functions.Worker.ItemTemplates
```

The csproj's `<PackageDownload>` item pulls the upstream pkg into the
global packages folder during restore, so the pack-time `HydrateDotnetTemplates`
target (in `eng/build/Workloads.Templates.DotNet.targets`) never reaches
the network. It runs `dotnet new install <local-nupkg-path>` against a
build-scoped template hive, reads the resulting `templatecache.json`,
filters to Functions item templates (`Classifications` contains
`Azure Function` and `tags.type == "item"`), strips
`bind` / `derived` / `computed` / `generated` / implicit / disabled
symbols, and projects the result into `dotnet-templates.json` (Templates
Workload Spec §5.3.1). If `Microsoft.Azure.Functions.Worker.ItemTemplates`
is staged in a private feed, configure the feed in `NuGet.Config` so
restore picks it up.

The dev-only flag `/p:SkipHydrateDotnetTemplates=true` produces a
pack with no catalog payload for offline iteration; it is **not** for
release builds.

## Layout

The package places its metadata under `tools/any/content/`. After
install:

```
<workload-home>/workloads/azure.functions.cli.workloads.templates.dotnet/<version>/
  tools/any/content/
    dotnet-templates.json   ← catalog + parameter index (spec §5.3.1)
    source.json             ← NuGet pin: { kind, packageId, version }
  workload.json
```

`tools/any/` is the canonical content-workload payload path. There are
no per-template file directories on disk — `dotnet-templates.json`
drives `func templates list` and `func new --template <id> --help`
fully offline, and the upstream NuGet pin in `source.json` drives the
CLI-managed dotnet template hive that scaffold (`dotnet new <id>`)
resolves against (Templates Workload Spec §5.3, §6.3).

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Templates workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/templates-workload-spec.md)
