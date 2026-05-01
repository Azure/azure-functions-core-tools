# Building a New Workload

This guide walks through building a workload for the Azure Functions Core Tools v5 CLI. A workload is a NuGet package that the CLI loads at runtime to extend its behavior — most commonly to provide `func init` / `func new` support for a specific language stack (e.g. Node.js, Python, Java), but a workload can also contribute brand-new subcommands.

> **Status**: the abstractions and DI host described below are in the tree as of this PR. The runtime loader and `func workload install` / `uninstall` commands land in a follow-up PR; until then, this document describes the contract workload authors can build against.

## Architecture

```
┌─────────────────────────────────┐
│  Func (the CLI executable)      │
│  Builds a host, loads workloads │
│  via AssemblyLoadContext        │
└──────────┬──────────────────────┘
           │ references (compile-time)
           ▼
┌─────────────────────────────────┐
│  Abstractions                   │
│  NuGet package with interfaces  │
│  IWorkload, FunctionsCliBuilder │
│  IProjectInitializer, …         │
└──────────┬──────────────────────┘
           │ referenced by (compile-time)
           ▼
┌─────────────────────────────────┐
│  Your Workload                  │
│  e.g. Workload.Node             │
│  NuGet package, loaded at       │
│  runtime via reflection         │
└─────────────────────────────────┘
```

**Key principle**: workloads reference only `Abstractions`, never `Func`. The CLI loads workloads dynamically — there is no compile-time dependency from the CLI to any workload.

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
     └── Every command registered through builder.RegisterCommand(...) is
         added as a root subcommand, tagged with the workload that registered it
4. Command dispatch
```

The shape mirrors WebJobs' `IWebJobsStartup` — `Configure(FunctionsCliBuilder)` is the workload's only seam, and everything beyond it is plain .NET DI.

## Quick Start

1. Create a class library project that targets `net10.0`
2. Reference `Azure.Functions.Cli.Abstractions`
3. Implement `IWorkload`
4. Inside `Configure`, register an `IProjectInitializer` (and/or contribute top-level commands via `builder.RegisterCommand(...)`, and any supporting services)
5. Build, package, and (once the loader ships) install with `func workload install`

## Step 1: Create the Project

```bash
mkdir src/Workload/Node
cd src/Workload/Node
```

Create the `Workload.Node.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../Abstractions/Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Azure.Functions.Cli.Workload.Node.Tests" />
  </ItemGroup>

</Project>
```

Add the standard build files (copy from an existing project): `Directory.Version.props`.

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
        // builder.RegisterCommand(new NodeTopLevelCommand());
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
        var projectPath    = context.WorkingDirectory.Info.FullName;

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
| `WorkloadContext` (base) | `WorkingDirectory` — the directory (`DirectoryInfo` + `WasExplicit` flag) the command is operating from. The CLI guarantees `WorkingDirectory.Info` exists before invoking your initializer, so you don't need to call `Directory.CreateDirectory`. Stack-specific concepts like dotnet's `.csproj` are not modelled here; workloads add their own options for those. |
| `InitContext` | `ProjectName`, `Language`, `Force` |

Workload-specific option values flow through the `ParseResult`, so the CLI never has to know about your options.

## Step 4 (optional): Contribute New Commands

For "feature" workloads that aren't tied to a stack — say a Durable Functions workload — register top-level commands through `builder.RegisterCommand(...)`. Workload commands derive from `FuncCommand` (in `Azure.Functions.Cli.Workloads`), which is parser-independent: you describe the command's name, description, options, arguments, and subcommands using lightweight descriptors, and read parsed values through a `FuncCommandInvocationContext` at execution time. The CLI tracks which workload each command came from for diagnostics and collision reporting.

```csharp
using Azure.Functions.Cli.Workloads;

internal sealed class DurableCommand : FuncCommand
{
    private static readonly FuncCommandOption<string> InstanceOption =
        new("--instance-id", "-i", "Orchestration instance id");

    public override string Name => "durable";

    public override string Description => "Manage Durable Functions";

    public override IReadOnlyList<FuncCommand> Subcommands =>
    [
        new ListInstancesCommand(),
        new StartNewCommand(),
    ];

    public override Task<int> ExecuteAsync(
        FuncCommandInvocationContext context,
        CancellationToken cancellationToken) => Task.FromResult(0);

    private sealed class ListInstancesCommand : FuncCommand
    {
        public override string Name => "get-instances";

        public override string Description => "List orchestration instances.";

        public override IReadOnlyList<FuncCommandOption> Options => [InstanceOption];

        public override Task<int> ExecuteAsync(
            FuncCommandInvocationContext context,
            CancellationToken cancellationToken)
        {
            var instanceId = context.GetValue(InstanceOption);
            // …list instances…
            return Task.FromResult(0);
        }
    }

    private sealed class StartNewCommand : FuncCommand
    {
        public override string Name => "start-new";

        public override string Description => "Start a new orchestration.";

        public override Task<int> ExecuteAsync(
            FuncCommandInvocationContext context,
            CancellationToken cancellationToken) => Task.FromResult(0);
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
        // Pick the overload that fits how your command is constructed:
        //   builder.RegisterCommand(new DurableCommand());        // simple instance
        //   builder.RegisterCommand<DurableCommand>();            // DI-constructed
        //   builder.RegisterCommand(sp => new DurableCommand(...)); // custom factory
        builder.RegisterCommand<DurableCommand>();
    }
}
```

This produces `func durable get-instances`, `func durable start-new`, and so on. Reading parsed values goes through the supplied `FuncCommandInvocationContext` using the **same descriptor instance** the command exposes through `Options` / `Arguments` — descriptors are looked up by reference identity, so don't construct a new descriptor every time you read a value.

> The host wraps your `FuncCommand` in an internal adapter that carries the owning `WorkloadInfo`, so `func` can identify the source workload in collision warnings and diagnostics. Don't register raw `System.CommandLine.Command` services through `builder.Services` — the parser only consumes commands registered through `RegisterCommand`.

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
dotnet sln add src/Workload/Node/Workload.Node.csproj
dotnet sln add test/Workload/Node.Tests/Workload.Node.Tests.csproj
```

Add path-scoped public + official CI pipelines in `eng/ci/` (mirror the abstractions pipelines as a starting point — workload-specific templates will land alongside the loader in a follow-up PR).

## Checklist

- [ ] New project `src/Workload/<Name>/Workload.<Name>.csproj`, references `Abstractions` only
- [ ] `IWorkload` with parameterless constructor and the five identity properties (`PackageId`, `PackageVersion`, `Type`, `DisplayName`, `Description`)
- [ ] `Configure(FunctionsCliBuilder)` registers an `IProjectInitializer` and/or top-level commands via `builder.RegisterCommand(...)`
- [ ] `IProjectInitializer.GetInitOptions()` returns any extra options the initializer needs
- [ ] Test project `test/Workload/<Name>.Tests/Workload.<Name>.Tests.csproj`
- [ ] `Directory.Version.props` created
- [ ] Added to the solution
- [ ] Public + official CI pipelines in `eng/ci/`
- [ ] `dotnet build` and `dotnet test` pass
