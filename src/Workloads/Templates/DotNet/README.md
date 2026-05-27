# Azure Functions .NET Templates

A `kind: content` workload that pins the upstream
`Microsoft.Azure.Functions.Worker.ItemTemplates` NuGet package for use
by the func CLI's `func new`. The workload itself ships only an index
(`dotnet-templates.json`) and a pin (`source.json`); template files are
resolved at `func new` time via `dotnet new` against a CLI-managed
template hive.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Templates.DotNet
# or by alias
func workload install dotnet-templates
```

See `proposed/templates-workload-spec.md` (docs branch) for the full design.
