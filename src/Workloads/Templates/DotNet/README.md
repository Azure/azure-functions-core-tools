# Azure Functions CLI: .NET templates workload

This package ships function-scaffold templates for .NET Azure Functions
projects, consumed by the `func` CLI's `func new` command. It is a
content workload: the package carries metadata files only, with no
entry-point assembly and no runtime services. `func new` resolves the
install directory by package id and reads the catalog directly.

Unlike the Node and Python templates workloads, the .NET workload does
**not** carry per-template source files. The template content itself
lives in the upstream `Microsoft.Azure.Functions.Worker.ItemTemplates`
NuGet package; this workload ships a fully-hydrated **catalog +
parameter index** (`dotnet-templates.json`) plus a **pin**
(`source.json`) recording the exact upstream version. At
`func workload install` time, the CLI provisions the pinned NuGet
package into the dotnet template hive; at `func new <id>` time the
CLI shells out to `dotnet new` against that offline-provisioned hive.

The DotNet workload has no extension-bundle channel; only the stable
channel ships in v1:

| Channel | Workload pkg version | Upstream NuGet pin                                |
|---------|----------------------|---------------------------------------------------|
| stable  | `1.0.0`              | `Microsoft.Azure.Functions.Worker.ItemTemplates`  |

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Templates.DotNet@1.0.0
# or by alias
func workload install dotnet-templates@1.0.0
```

Multiple templates versions coexist via `--force`.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
- [Templates workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/templates-workload-spec.md)
