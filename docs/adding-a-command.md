# Adding a New Command

This guide walks through adding a new command to the Azure Functions Core Tools v5 CLI.

## AI-Assisted Scaffolding

You can use GitHub Copilot or another AI coding agent to scaffold a new command.
The repo includes agent instructions (`AGENTS.md`) with
conventions and patterns. Try a prompt like:

> Scaffold a new "func deploy" command with options for app name and resource group.
> Follow the command guide in docs/adding-a-command.md, register it in BuiltInCommands.cs,
> and add a test file.

## Quick Start

1. Create a new command class in `src/Func.Cli/Commands/`
2. Register it in `src/Func.Cli/Hosting/BuiltInCommands.cs`
3. Add tests in `test/Func.Cli.Tests/`

## Step 1: Create the Command Class

Create a new file in `src/Func.Cli/Commands/`. All commands extend `BaseCommand`.

```csharp
// src/Func.Cli/Commands/DeployCommand.cs

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

internal class DeployCommand : BaseCommand
{
    // Define options as instance properties
    public Option<string> AppNameOption { get; } = new("--app-name", "-a")
    {
        Description = "The Azure Functions app name to deploy to",
        Required = true
    };

    public Option<bool> DryRunOption { get; } = new("--dry-run")
    {
        Description = "Preview the deployment without making changes"
    };

    private readonly IInteractionService _interaction;

    public DeployCommand(IInteractionService interaction)
        : base("deploy", "Deploy the function app to Azure")
    {
        _interaction = interaction;

        // Add options to the command
        Options.Add(AppNameOption);
        Options.Add(DryRunOption);

        // Optional: add [path] argument for project directory
        AddPathArgument();

        // Wire up the handler
        SetAction(async (parseResult, cancellationToken) =>
        {
            ApplyPath(parseResult); // handles [path] → Directory.SetCurrentDirectory

            var appName = parseResult.GetValue(AppNameOption)!;
            var dryRun = parseResult.GetValue(DryRunOption);

            await ExecuteAsync(appName, dryRun, cancellationToken);
        });
    }

    private async Task ExecuteAsync(string appName, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            _interaction.WriteWarning("Dry run mode — no changes will be made.");
        }

        await _interaction.StatusAsync($"Deploying to '{appName}'...", async () =>
        {
            // Your deployment logic here
            await Task.Delay(1000, cancellationToken); // placeholder
        });

        _interaction.WriteSuccess($"Deployed to '{appName}'.");
    }
}
```

### Key Conventions

- **Constructor injection**: Take `IInteractionService` (and `IWorkloadManager` if needed)
- **Instance option properties**: Options are `public Option<T> XOption { get; } = new(...)` — instance, not static. Tests and other code reach them through the resolved command instance (DI). Avoid `static readonly` so each command instance owns its own options and parallel test runs cannot share mutable parser state.
- **`SetAction`**: Wire handler in the constructor, not via override
- **`ApplyPath`**: Call this first if you added `AddPathArgument()`
- **Never use `Console.Write*`**: Always go through `IInteractionService`
- **Throw `GracefulException`** for user-facing errors (not `Exception`)

### Options vs Arguments

```csharp
// Options: named, optional by default
public Option<string> NameOption { get; } = new("--name", "-n")
{
    Description = "The resource name",
    Required = true  // make it required
};

// Options with defaults
public Option<int> TimeoutOption { get; } = new("--timeout")
{
    Description = "Timeout in seconds",
    DefaultValueFactory = _ => 30
};

// Arguments: positional (use BaseCommand.AddPathArgument for [path])
var arg = new Argument<string>("resource") { Description = "The resource to deploy" };
Arguments.Add(arg);
```

## Step 2: Register in BuiltInCommands.cs

Open `src/Func.Cli/Hosting/BuiltInCommands.cs` and register the command as a `BaseCommand` so the parser picks it up. Built-in commands also implement the `IBuiltInCommand` marker interface so the parser distinguishes them from workload-contributed commands:

```csharp
// src/Func.Cli/Commands/DeployCommand.cs
internal class DeployCommand : BaseCommand, IBuiltInCommand
{
    // …
}

// src/Func.Cli/Hosting/BuiltInCommands.cs
public static IServiceCollection AddBuiltInCommands(this IServiceCollection services)
{
    // ... existing registrations ...
    services.AddSingleton<BaseCommand, DeployCommand>();   // ← add this
    return services;
}
```

`Parser.CreateCommand` resolves every `BaseCommand` from DI, partitions them into built-ins (`IBuiltInCommand`) and workload-contributed (`ExternalCommand`), and adds them to the root tree. The help system picks up the new command automatically.

## Step 3: Add Tests

Create a test file in `test/Func.Cli.Tests/Commands/`:

```csharp
// test/Func.Cli.Tests/Commands/DeployCommandTests.cs

using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class DeployCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void Deploy_RegisteredInCommandTree()
    {
        var root = Parser.CreateCommand(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();
        Assert.Contains("deploy", names);
    }

    [Fact]
    public void Deploy_HasRequiredOptions()
    {
        var cmd = new DeployCommand(_interaction);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();
        Assert.Contains("--app-name", optionNames);
        Assert.Contains("--dry-run", optionNames);
    }

    [Fact]
    public async Task Deploy_DryRun_ShowsWarning()
    {
        var cmd = new DeployCommand(_interaction);
        // Invoke via Parser to get full integration test
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse("deploy --app-name myapp --dry-run");
        await result.InvokeAsync();

        Assert.Contains(_interaction.Lines, l => l.Contains("Dry run"));
    }
}
```

### Testing Tips

- **`TestInteractionService`** captures all output in `.Lines` and `.ErrorLines`
- Use `Parser.CreateCommand(_interaction)` to test the full command tree
- Use `root.Parse("args here")` to simulate CLI invocation
- For workload-dependent commands, pass an `IWorkloadManager` (or `WorkloadManager` with a temp directory)

## Step 4: Run and Verify

```bash
# Build
dotnet build

# Run tests
dotnet test test/Func.Cli.Tests/

# Try it locally
dotnet run --project src/Func.Cli/ -- deploy --app-name myapp --dry-run
```

## Adding Nested Subcommands

For grouped commands (like `func workload install`), create a parent command:

```csharp
public class AzureCommand : BaseCommand
{
    public AzureCommand(IInteractionService interaction)
        : base("azure", "Azure resource management commands")
    {
        // Add subcommands
        Subcommands.Add(new AzureLoginCommand(interaction));
        Subcommands.Add(new AzureListCommand(interaction));
    }
}

public class AzureLoginCommand : BaseCommand
{
    public AzureLoginCommand(IInteractionService interaction)
        : base("login", "Log in to Azure")
    {
        SetAction(async (parseResult, ct) =>
        {
            // login logic
        });
    }
}
```

This produces: `func azure login`, `func azure list`.

## Checklist

- [ ] Command class in `src/Func.Cli/Commands/`
- [ ] Implements `IBuiltInCommand` and registered as a `BaseCommand` in `BuiltInCommands.cs`
- [ ] Uses `IInteractionService` for all I/O
- [ ] Uses `GracefulException` for user-facing errors
- [ ] Tests cover registration, options, and happy path
- [ ] `dotnet build` succeeds with no warnings
- [ ] `dotnet test` passes
