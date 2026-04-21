// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Packages an Azure Functions project into a zip ready for deployment by
/// routing to the workload that owns the project's runtime.
/// </summary>
public class PackCommand : BaseCommand
{
    public static readonly Option<string?> OutputOption = new("--output", "-o")
    {
        Description = "The directory to place the output zip file in"
    };

    public static readonly Option<bool> NoBuildOption = new("--no-build")
    {
        Description = "Skip building the project before packaging"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadHost _workloadHost;

    public PackCommand(IInteractionService interaction, IWorkloadHost workloadHost)
        : base("pack", "Package the function app into a zip ready for deployment.")
    {
        _interaction = interaction;
        _workloadHost = workloadHost;

        AddPathArgument();
        Options.Add(OutputOption);
        Options.Add(NoBuildOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult);
        var projectPath = Directory.GetCurrentDirectory();

        var detection = await _workloadHost.DetectProjectAsync(projectPath, cancellationToken);
        if (detection is null)
        {
            _interaction.WriteError("Could not determine the worker runtime for this project.");
            _interaction.WriteMarkupLine("[grey]Install the matching workload, or run from a project directory.[/]");
            return 1;
        }

        var (workload, _) = detection.Value;
        await using var client = await _workloadHost.StartByIdAsync(workload.Manifest.Id, cancellationToken);

        var result = await client.InvokeAsync(
            WorkloadProtocol.Methods.PackRun,
            new PackRunParams(
                ProjectPath: projectPath,
                OutputPath: parseResult.GetValue(OutputOption),
                NoBuild: parseResult.GetValue(NoBuildOption)),
            WorkloadJsonContext.Default.PackRunParams,
            WorkloadJsonContext.Default.PackRunResult,
            cancellationToken);

        _interaction.WriteSuccess($"Packed: {result.OutputPath}");
        return 0;
    }
}
