# Azure.Functions.Cli.Workloads.DotNet

## Target frameworks

`func init` supports C# and F# projects targeting `net10.0` (the default).
For C# projects, select .NET 11 explicitly:

```shell
func init MyApp --stack dotnet --language csharp --target-framework net11.0
```

As in v4, .NET 11 isolated project initialization is not yet supported for F#.
Use C# or target .NET 10 instead.

Creating .NET 11 projects requires a .NET 11 SDK and a version of
`Microsoft.Azure.Functions.Worker.ProjectTemplates` that supports `net11.0`.
The workload installs that template package into its private hive. Existing
projects are built with the installed `dotnet` SDK when running `func start`
or `func run`; `--no-build` uses the existing build output. The CLI and workload
binaries continue to target .NET 10.

## 1.0.0-preview.1

- Initial scaffold of the .NET workload (entry point + stub project initializer).