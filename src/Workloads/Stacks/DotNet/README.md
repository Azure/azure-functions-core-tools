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

The HTTP and timer trigger templates declare the standard add-reference post
action (`B17581D1-C5C9-4489-8F0A-004BE667B814`) for their extension packages.
The workload processes it for the same templates by running `dotnet add <target>
package <id>` (with `--version` when set) or `dotnet add <target> reference
<project>`. Targets come from `targetFiles` matches; otherwise, as in `dotnet
new`, the single project file in the nearest directory at or above the output
directory is used, filtered by the literal `projectFileExtensions`. Multiple
projects there are an error; with none, existing `targetFiles` paths are used.
Processing stops at the first failure. Framework references are unsupported.

The post-action dispatcher is not yet wired into `func init` or `func new`.
This registration and template declaration do not change their current behavior.

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