// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Initializes a new Azure Functions project. The actual scaffolding is
/// delegated to an <see cref="IProjectInitializer"/> contributed by a workload.
/// Each registered initializer may also contribute additional options to this
/// command (e.g. dotnet's <c>--target-framework</c>).
/// </summary>
public class InitCommand : BaseCommand
{
    public static readonly Option<string?> WorkerRuntimeOption = new("--worker-runtime", "-w")
    {
        Description = "The worker runtime for the project"
    };

    public static readonly Option<string?> NameOption = new("--name", "-n")
    {
        Description = "The name of the function app project"
    };

    public static readonly Option<string?> LanguageOption = new("--language", "-l")
    {
        Description = "The programming language (e.g., C#, F#, JavaScript, TypeScript, Python)"
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Force initialization even if the folder is not empty"
    };

    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<IProjectInitializer> _initializers;
    private readonly IReadOnlyList<IWorkload> _workloads;

    public InitCommand(
        IInteractionService interaction,
        IEnumerable<IProjectInitializer> initializers,
        IReadOnlyList<IWorkload> workloads)
        : base("init", "Initialize a new Azure Functions project.")
    {
        _interaction = interaction;
        _initializers = initializers.ToList();
        _workloads = workloads;

        AddPathArgument();
        Options.Add(WorkerRuntimeOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        // Workload-contributed options are attached after built-ins so they
        // appear as a clearly-grouped block in --help output.
        foreach (var initializer in _initializers)
        {
            foreach (var option in initializer.GetInitOptions())
            {
                Options.Add(option);
            }
        }
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        var workerRuntime = parseResult.GetValue(WorkerRuntimeOption);

        var initializer = SelectInitializer(workerRuntime);
        if (initializer is null)
        {
            WorkloadHints.WriteNoMatchingWorkload(
                _interaction,
                _workloads,
                actionDescription: "initialize a project",
                requestedRuntime: workerRuntime);
            return 1;
        }

        var context = new ProjectInitContext(
            ProjectPath: Directory.GetCurrentDirectory(),
            ProjectName: parseResult.GetValue(NameOption),
            Language: parseResult.GetValue(LanguageOption),
            Force: parseResult.GetValue(ForceOption));

        await initializer.InitializeAsync(context, parseResult, cancellationToken);

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Project initialized for ")
            .Code(initializer.WorkerRuntime)
            .Muted("."));

        return 0;
    }

    private IProjectInitializer? SelectInitializer(string? workerRuntime)
    {
        if (_initializers.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrEmpty(workerRuntime))
        {
            // Auto-select only when exactly one initializer is registered.
            return _initializers.Count == 1 ? _initializers[0] : null;
        }

        return _initializers.FirstOrDefault(i => i.CanHandle(workerRuntime));
    }
}
