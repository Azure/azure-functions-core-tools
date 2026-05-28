# Azure Functions CLI: Node templates workload

This package ships function-scaffold templates for Node.js Azure
Functions projects (**v2 template-engine schema, Node v4 programming
model**), consumed by the `func` CLI's `func new` command. It is a
**content workload** (Workload Spec §3; Templates Workload Spec §4.1):
the package carries template files only, with no entry-point assembly
and no runtime services. `func new` resolves the install directory by
package id and reads the template payload directly.

> **Template source.** The Node templates workload **does not** snapshot
> content from the extension-bundle CDN — the upstream bundle's
> `StaticContent/v2/templates/templates.json` does not yet publish Node
> entries (only Python). The Node v2 template content is therefore
> authored statically in this repo under
> `src/Workloads/Templates/Node/content/v2/`. See Templates Workload
> Spec §6.1.

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

The bundle id is the value `func new` expects in the project's
`host.json` `extensionBundle.id` at scaffold time. Channel selection
at `func new` is implicit — derived from the project's `host.json`
bundle id.

> **Per-channel template subsetting** is **on** by default for the
> Node workload. At pack time the build fetches `bin/extensions.json`
> from the latest **listed** bundle of `$(TemplatesChannel)`
> (resolved from the channel's CDN `index.json`) using HTTP Range
> requests (~10 KB rather than the full 150 MB bundle) and drops any
> template whose required bindings aren't present in that channel's
> bundle. The mapping of templates to required bindings is committed
> in `src/Workloads/Templates/Node/content/v2/templates/_bindings.json`.
> See Templates Workload Spec §4.3 / §6.1. Snapshot at the time of
> writing: stable `4.32.0` → 31 of 33 (no `mcpPromptTrigger`),
> preview `4.42.0` → 33 of 33, experimental `4.6.0` → 31 of 33.

## Bundle compatibility

This workload declares a minimum compatible extension-bundle version
(`[4.0.0,)`). `func new` validates the project's resolved bundle
version against this constraint and surfaces incompatibilities as a
warning or error (Templates Workload Spec §4.4.4). The constraint is
recorded in two locations that both derive from the single
`$(MinBundleVersionRange)` MSBuild property, so they cannot drift:

- `tools/any/content/templates-workload.json` — CLI-owned sibling
  manifest. Authoritative. Generated at pack time by the
  `GenerateTemplatesWorkloadJson` target.
- Nuspec `minBundle:[4.0.0,)` tag — discoverable via
  `func workload search` / `list`.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Templates.Node@1.0.0
# or by alias
func workload install node-templates@1.0.0
```

Preview / experimental channels resolve via the matching prerelease
label:

```bash
func workload install node-templates@1.0.0-preview
func workload install node-templates@1.0.0-experimental
```

Multiple templates versions coexist via `--force` (Workload Spec §4.6).

## Build-time channel selection

The workload pkg version's prerelease label is driven by
`$(TemplatesChannel)` in `Directory.Version.props`:

```bash
dotnet pack ... /p:TemplatesChannel=preview
dotnet pack ... /p:TemplatesChannel=experimental
```

For Node, channel does **not** select a CDN source — `$(TemplatesContentSource)`
is `static` and the content always comes from `content/v2/` in this
repo. `$(SourceBundleVersion)` is unused for Node.

## Layout

The package places the template payload under `tools/any/content/v2/`,
following the v2 template-engine schema layout (Templates Workload
Spec §5.2):

```
<workload-home>/workloads/azure.functions.cli.workloads.templates.node/<version>/
  tools/any/content/
    templates-workload.json     ← min-bundle sibling manifest
    v2/
      bindings/userPrompts.json
      resources/Resources.json
      templates/templates.json  ← NewTemplate[] (jobs/actions DSL)
  workload.json
```

Each `NewTemplate` entry carries its per-template file contents
**inline** in a `files: { <filename>: <contents> }` map; the
`WriteToFile` action writes the file to
`src/functions/$(FUNCTION_NAME_INPUT).<js|ts>` after the engine
substitutes the `$(FUNCTION_NAME_INPUT)` token (Templates Workload
Spec §5.2 / §5.4).

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Templates workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/templates-workload-spec.md)
- [v2 template engine schema (Azure/azure-functions-templates)](https://github.com/Azure/azure-functions-templates/tree/dev/Docs)
