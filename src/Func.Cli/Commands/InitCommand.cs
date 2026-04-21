// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Initializes a new Azure Functions project by routing to an installed
/// out-of-process workload that owns the requested worker runtime.
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
    private readonly IWorkloadHost _workloadHost;

    public InitCommand(IInteractionService interaction, IWorkloadHost workloadHost)
        : base("init", "Initialize a new Azure Functions project.")
    {
        _interaction = interaction;
        _workloadHost = workloadHost;

        AddPathArgument();
        Options.Add(WorkerRuntimeOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        var workerRuntime = parseResult.GetValue(WorkerRuntimeOption);
        var name = parseResult.GetValue(NameOption);
        var language = parseResult.GetValue(LanguageOption);
        var force = parseResult.GetValue(ForceOption);

        var runtimes = _workloadHost.GetAvailableRuntimes();
        if (runtimes.Count == 0)
        {
            ShowNoWorkloadsHelp();
            return 1;
        }

        if (string.IsNullOrEmpty(workerRuntime))
        {
            workerRuntime = runtimes.Count == 1
                ? runtimes[0]
                : await _interaction.PromptForSelectionAsync("Select a worker runtime", runtimes, cancellationToken);
        }

        await using var client = await _workloadHost.StartForRuntimeAsync(workerRuntime!, cancellationToken);

        var result = await client.InvokeAsync(
            WorkloadProtocol.Methods.ProjectInit,
            new ProjectInitParams(
                ProjectPath: Directory.GetCurrentDirectory(),
                WorkerRuntime: workerRuntime!,
                Language: language,
                ProjectName: name,
                Force: force,
                Extra: null),
            WorkloadJsonContext.Default.ProjectInitParams,
            WorkloadJsonContext.Default.ProjectInitResult,
            cancellationToken);

        _interaction.WriteSuccess($"Project initialized ({result.FilesCreated.Count} files).");
        foreach (var file in result.FilesCreated)
        {
            _interaction.WriteMarkupLine($"  [grey]+[/] {file}");
        }
        return 0;
    }

    private void ShowNoWorkloadsHelp()
    {
        _interaction.WriteError("No language workloads installed.");
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("[grey]Install a workload to initialize a project:[/]");
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("  [white]func workload install sample[/]   [grey]Demo workload (in-tree)[/]");
        _interaction.WriteMarkupLine("  [white]func workload install dotnet[/]   [grey]C#, F# (not yet wired)[/]");
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("[grey]Run[/] [white]func workload search[/] [grey]to discover available workloads.[/]");
    }
}

