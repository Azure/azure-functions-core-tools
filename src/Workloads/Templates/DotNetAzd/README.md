# Azure Functions CLI: .NET (azd) templates workload

This package ships a **full-project azd quickstart** template for .NET
(isolated worker) Azure Functions projects, consumed by the `func`
CLI's v2 template engine. It is a `kind: content` workload: the package
carries template content only, with no entry-point assembly and no
runtime services. The CLI resolves the install directory by package id
and reads the v2 payload directly.

Unlike the Node and Python templates workloads, this workload scaffolds
an **entire project tree** (function app, Bicep infrastructure, azd
config, dev-container / VS Code settings) rather than a single function
file. The payload is sourced from the
[functions-quickstart-dotnet-azd](https://github.com/Azure-Samples/functions-quickstart-dotnet-azd)
sample.

## No extension-bundle dependency

.NET isolated-worker functions resolve extensions via NuGet package
references in the project's `.csproj`, not via an extension bundle. This
workload therefore has **no bundle channel axis** and ships **no**
`templates-workload.json` min-bundle manifest.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Templates.DotNetAzd@1.0.0
# or by alias
func workload install dotnet-azd-templates@1.0.0
```

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Templates workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/templates-workload-spec.md)
- [v2 template engine schema (Azure/azure-functions-templates)](https://github.com/Azure/azure-functions-templates/tree/dev/Docs)
- [Source sample: functions-quickstart-dotnet-azd](https://github.com/Azure-Samples/functions-quickstart-dotnet-azd)
