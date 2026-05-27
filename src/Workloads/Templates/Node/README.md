# Azure Functions CLI: Node templates workload

This package ships function-scaffold templates for Node.js Azure
Functions projects, consumed by the `func` CLI's `func new` command.
It is a **content workload** (Workload Spec §3; Templates Workload
Spec §4.1): the package carries template files only, with no
entry-point assembly and no runtime services. `func new` resolves the
install directory by package id and reads the template payload directly.

The workload pkg version is independent of the source bundle version
(Templates Workload Spec §4.4.1). The channel a workload was built
from is encoded in the pkg version's prerelease label, and that
channel maps 1:1 to an extension-bundle id (Templates Workload Spec
§4.4.2):

| Channel       | Workload pkg version   | Bundle id                                                          |
|---------------|------------------------|--------------------------------------------------------------------|
| stable        | `1.0.0`                | `Microsoft.Azure.Functions.ExtensionBundle`                        |
| preview       | `1.0.0-preview`        | `Microsoft.Azure.Functions.ExtensionBundle.Preview`                |
| experimental  | `1.0.0-experimental`   | `Microsoft.Azure.Functions.ExtensionBundle.Experimental`           |

The bundle id is both the upstream extension bundle the workload's
templates were snapshotted from at build time and the value `func new`
expects in the project's `host.json` `extensionBundle.id` at scaffold
time. Channel selection at `func new` is implicit — derived from the
project's `host.json` bundle id.

## Bundle compatibility

This workload declares a minimum compatible extension-bundle version
(`[4.0.0,)`). `func new` validates the project's resolved bundle
version against this constraint and surfaces incompatibilities as a
warning or error (Templates Workload Spec §4.4.4). The constraint is
recorded in three locations that all derive from the single
`$(MinBundleVersionRange)` MSBuild property, so they cannot drift:

- `tools/any/content/templates-workload.json` — CLI-owned sibling
  manifest. Authoritative. Generated at pack time by the
  `GenerateTemplatesWorkloadJson` target.
- Nuspec `minBundle:[4.0.0,)` tag — discoverable via
  `func workload search` / `list`.
- Nuspec `<description>` sentence — surfaced by `func workload list`.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Templates.Node@1.0.0
# or by alias
func workload install templates-node@1.0.0
```

Preview / experimental channels resolve via the matching prerelease
label:

```bash
func workload install templates-node@1.0.0-preview
func workload install templates-node@1.0.0-experimental
```

Multiple templates versions coexist via `--force` (Workload Spec §4.6).

## Build-time channel selection

The source bundle id used at pack time is driven by
`$(TemplatesChannel)` in `Directory.Version.props`:

```bash
dotnet pack ... /p:TemplatesChannel=preview
dotnet pack ... /p:TemplatesChannel=experimental
```

`$(SourceBundleVersion)` selects the bundle version whose
`StaticContent/v1` subtree is snapshotted at build time. It is
recorded as build provenance only; it does not derive the templates
pkg version (Templates Workload Spec §4.4.1).

## Layout

The package places the template payload under `tools/any/content/v1/`,
matching the upstream extension bundle's `StaticContent/v1/` subtree
with the `StaticContent/` wrapper stripped (Templates Workload Spec
§5.2). Node has no v2 programming model, so no `v2/` directory is
shipped. After install:

```
<workload-home>/workloads/azure.functions.cli.workloads.templates.node/<version>/
  tools/any/content/
    templates-workload.json     ← min-bundle sibling manifest
    v1/
      bindings/bindings.json
      resources/Resources.json
      resources/Resources.<locale>.json
      templates/templates.json  ← Template[] with files inline
  workload.json
```

Template files are carried **inline** inside each `Template` entry's
`files: { <filename>: <contents> }` map; there are no separate
per-template file directories on disk (Templates Workload Spec §5.2).
`tools/any/` is the canonical content-workload payload path.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Templates workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/templates-workload-spec.md)
