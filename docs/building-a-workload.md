# Building a New Workload

This guide walks through building a workload for the Azure Functions Core Tools v5 CLI. A workload is a NuGet package that the CLI loads at runtime to extend its behavior — most commonly to provide `func init` / `func new` support for a specific language stack (e.g. Node.js, Python, Java), but a workload can also contribute brand-new subcommands.

> **Status**: the abstractions and DI host described below are in the tree as of this PR. The runtime loader and `func workload install` / `uninstall` commands land in a follow-up PR; until then, this document describes the contract workload authors can build against.

## Architecture

```
┌─────────────────────────────────┐
│  Func.Cli (the CLI executable)  │
│  Builds a host, loads workloads │
│  via AssemblyLoadContext        │
└──────────┬──────────────────────┘
           │ references (compile-time)
           ▼
┌─────────────────────────────────┐
│  Func.Cli.Abstractions          │
│  NuGet package with interfaces  │
│  IWorkload, FunctionsCliBuilder│
│  IProjectInitializer, …         │
└──────────┬──────────────────────┘
           │ referenced by (compile-time)
           ▼
┌─────────────────────────────────┐
│  Your Workload                  │
│  e.g. Func.Cli.Workload.Node    │
│  NuGet package, loaded at       │
│  runtime via reflection         │
└─────────────────────────────────┘
```

**Key principle**: workloads reference only `Func.Cli.Abstractions`, never `Func.Cli`. The CLI loads workloads dynamically — there is no compile-time dependency from the CLI to any workload.

## How a Workload Plugs In

The CLI builds a `HostApplicationBuilder` at startup. Each installed workload is loaded into its own `AssemblyLoadContext`, instantiated, and handed a `FunctionsCliBuilder`:

```
1. CLI builds the host (HostApplicationBuilder)
2. For each entry in ~/.azure-functions/workloads.json:
     ├── Load the workload assembly into an isolated AssemblyLoadContext
     ├── Activate the IWorkload type
     └── Call workload.Configure(builder)        ← workload registers services here
3. CLI builds the root command tree from DI
     ├── Built-in commands resolve workload-contributed services they consume
     │   (e.g. InitCommand consumes IEnumerable<IProjectInitializer>)
     └── Every Command registered in DI is added as a root subcommand
4. Command dispatch
```

The shape mirrors WebJobs' `IWebJobsStartup` — `Configure(FunctionsCliBuilder)` is the workload's only seam, and everything beyond it is plain .NET DI.

## Quick Start

1. Create a class library project that targets `net10.0`
2. Reference `Azure.Functions.Cli.Abstractions`
3. Implement `IWorkload`
4. Inside `Configure`, register an `IProjectInitializer` (and/or top-level `Command` services, and any supporting services)
5. Build, package, and (once the loader ships) install with `func workload install`

## Step 1: Create the Project

```bash
mkdir src/Func.Cli.Workload.Node
cd src/Func.Cli.Workload.Node
```

Create the `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Azure.Functions.Cli.Workload.Node</AssemblyName>
    <RootNamespace>Azure.Functions.Cli.Workload.Node</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>true</IsPackable>
    <PackageId>Azure.Functions.Cli.Workload.Node</PackageId>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Func.Cli.Abstractions\Func.Cli.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Func.Cli.Workload.Node.Tests" />
  </ItemGroup>

</Project>
```

Add the standard build files (copy from an existing project): `Directory.Build.props`, `Directory.Build.targets`, `Directory.Version.props`.

## Step 2: Implement IWorkload

`IWorkload` is the entry point the CLI activates. **It must have a parameterless constructor.**

```csharp
// NodeWorkload.cs
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workload.Node;

public sealed class NodeWorkload : IWorkload
{
    public string PackageId      => "Azure.Functions.Cli.Workload.Node";
    public string PackageVersion => "1.0.0";   // typically read from assembly metadata
    public string DisplayName    => "Node.js";
    public string Description    => "func init / func new support for Node.js and TypeScript.";

    public void Configure(FunctionsCliBuilder builder)
    {
        builder.Services.AddSingleton<IProjectInitializer, NodeProjectInitializer>();
        // builder.Services.AddSingleton<Command>(new NodeTopLevelCommand());
        // …any other services your providers depend on
    }
}
```

The properties surface in `func workload list`.

## Step 3: Implement IProjectInitializer

`IProjectInitializer` extends `func init`. Each registered initializer owns a stack, contributes any extra options it needs, and scaffolds the project.

```csharp
// NodeProjectInitializer.cs
using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Node;

internal sealed class NodeProjectInitializer : IProjectInitializer
{
    public string Stack => "node";

    public IReadOnlyList<string> SupportedLanguages => ["JavaScript", "TypeScript"];

    public bool CanHandle(string stack) =>
        stack.Equals("node",       StringComparison.OrdinalIgnoreCase) ||
        stack.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
        stack.Equals("typescript", StringComparison.OrdinalIgnoreCase);

    // Workload-specific options. Attached to `func init` during command construction
    // and visible in --help. Read back inside InitializeAsync via the ParseResult.
    public Option<string> PackageManagerOption { get; } = new("--package-manager")
    {
        Description = "The package manager to use (npm, yarn, pnpm)",
        DefaultValueFactory = _ => "npm"
    };

    public IReadOnlyList<Option> GetInitOptions() => [PackageManagerOption];

    public async Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        var packageManager = parseResult.GetValue(PackageManagerOption) ?? "npm";
        var language       = context.Language ?? "JavaScript";
        var projectPath    = context.ProjectPath;

        Directory.CreateDirectory(projectPath);

        await File.WriteAllTextAsync(
            Path.Combine(projectPath, "package.json"),
            GeneratePackageJson(context.ProjectName ?? "my-functions-app"),
            cancellationToken);

        // …host.json, local.settings.json, tsconfig.json, etc.
    }

    // ... helper methods to generate file contents ...
}
```

### Contexts

`func init` (and other built-in commands) pass the workload a context object. Common inputs live on the `WorkloadContext` base record; command-specific inputs live on the derived record.

| Type | Provides |
|------|----------|
| `WorkloadContext` (base) | `ProjectPath` — the directory the command is operating on |
| `InitContext` | `ProjectName`, `Language`, `Force` |

Workload-specific option values flow through the `ParseResult`, so the CLI never has to know about your options.

## Step 4 (optional): Contribute New Commands

For "feature" workloads that aren't tied to a stack — say a Durable Functions workload — register `Command` instances directly in DI alongside (or instead of) an `IProjectInitializer`. The CLI picks up every `Command` registered in the container and attaches it to the root.

```csharp
internal sealed class DurableCommand : Command
{
    public DurableCommand() : base("durable", "Manage Durable Functions")
    {
        Subcommands.Add(new Command("get-instances", "List orchestration instances"));
        Subcommands.Add(new Command("start-new",     "Start a new orchestration"));
    }
}

public sealed class DurableWorkload : IWorkload
{
    public string PackageId      => "Azure.Functions.Cli.Workload.Durable";
    public string PackageVersion => "1.0.0";
    public string DisplayName    => "Durable Functions";
    public string Description    => "Durable Functions management commands.";

    public void Configure(FunctionsCliBuilder builder)
    {
        builder.Services.AddSingleton<Command>(new DurableCommand());
    }
}
```

This produces `func durable get-instances`, `func durable start-new`, and so on.

## Reading Workload-Specific Options

Built-in commands collect options from every registered provider during command construction, so they appear in `--help` automatically. Inside `InitializeAsync` (or any other provider method), read your option values from the supplied `ParseResult`:

```csharp
var packageManager = parseResult.GetValue(PackageManagerOption);
```

This is how workloads read their own options without the CLI needing to know about them.

## Testing

The contract is plain DI, so tests don't need the host:

```csharp
[Fact]
public void Configure_RegistersInitializer()
{
    var services = new ServiceCollection();
    var builder  = new TestBuilder(services);

    new NodeWorkload().Configure(builder);

    using var sp = services.BuildServiceProvider();
    var initializers = sp.GetServices<IProjectInitializer>().ToList();
    Assert.Single(initializers);
    Assert.IsType<NodeProjectInitializer>(initializers[0]);
}

private sealed class TestBuilder(IServiceCollection services) : FunctionsCliBuilder
{
    public override IServiceCollection Services { get; } = services;
}
```

Initializers are themselves easy to unit-test: drive them with a temp directory and a synthetic `ParseResult`.

## Solution + CI

Add the project (and its test project) to the solution:

```bash
dotnet sln add src/Func.Cli.Workload.Node/Func.Cli.Workload.Node.csproj
dotnet sln add test/Func.Cli.Workload.Node.Tests/Func.Cli.Workload.Node.Tests.csproj
```

Add path-scoped public + official CI pipelines in `eng/ci/` (mirror the abstractions pipelines as a starting point — workload-specific templates will land alongside the loader in a follow-up PR).

## Checklist

- [ ] New project in `src/Func.Cli.Workload.<Name>/`, references `Func.Cli.Abstractions` only
- [ ] `IWorkload` with parameterless constructor and the five identity properties (`PackageId`, `PackageVersion`, `Type`, `DisplayName`, `Description`)
- [ ] `Configure(FunctionsCliBuilder)` registers an `IProjectInitializer` and/or top-level `Command` services
- [ ] `IProjectInitializer.GetInitOptions()` returns any extra options the initializer needs
- [ ] Test project in `test/Func.Cli.Workload.<Name>.Tests/`
- [ ] `Directory.Build.props`, `Directory.Build.targets`, `Directory.Version.props` created
- [ ] Added to the solution
- [ ] Public + official CI pipelines in `eng/ci/`
- [ ] `dotnet build` and `dotnet test` pass
