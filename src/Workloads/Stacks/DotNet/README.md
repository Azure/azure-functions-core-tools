# Azure.Functions.Cli.Workloads.DotNet

## Bundled templates

The workload package includes the project, HTTP trigger, and timer trigger templates
from `Workloads.Templates.DotNet` under `content/templates`. Its generated
`workload.json` declares that directory as `templateRoot` so the CLI template engine
can discover the templates after workload installation.

The project template declares the standard NuGet restore post action
(`210D431B-A78B-4D2F-B762-4ED3E3EA9025`). The workload registers a processor
for `dotnet` templates tagged with C# or F# (including `csharp`/`fsharp`
aliases, case-insensitively). The optional `files` argument accepts source-file
glob patterns as a semicolon-delimited string or JSON array, following
`dotnet new`. Unique matched targets restore in pattern and file-change order;
otherwise primary outputs are used. Targets resolving outside the output directory
are rejected before any restore. No targets is a success.
Every target is attempted even after a restore failure; cancellation propagates.
The post-action reporter is currently a no-op, so restore output and detailed
errors are discarded; the dispatcher still displays the template's manual
instructions on failure.

The post-action dispatcher is not yet wired into `func init` or `func new`.
This registration and template declaration do not change their current behavior.
Item-template package-reference actions are separate and are not implemented by
the restore processor.

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
or `func run`; `--no-build` uses the existing build output. It runs MSBuild's
`GetTargetPath` target to locate that output without compiling. Successful lookups
are silent; failed lookups include the captured diagnostics. The CLI and workload
binaries target .NET 11.

## 1.0.0-preview.1

- Initial scaffold of the .NET workload (entry point + stub project initializer).