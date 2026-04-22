// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Packages an Azure Functions project into a zip ready for deployment.
/// The full implementation requires a language workload — this defines
/// the command skeleton and options.
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

    public PackCommand(IInteractionService interaction)
        : base("pack", "Package the function app into a zip ready for deployment.")
    {
        _interaction = interaction;

        AddPathArgument();
        Options.Add(OutputOption);
        Options.Add(NoBuildOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult);

        var projectPath = Directory.GetCurrentDirectory();

        // Verify this is a Functions project
        if (!File.Exists(Path.Combine(projectPath, "host.json")))
        {
            _interaction.WriteError("No Azure Functions project found. Run func init first.");
            return Task.FromResult(1);
        }

        // Detect the runtime
        var detectedRuntime = ProjectDetector.DetectRuntime(projectPath);
        if (detectedRuntime is null)
        {
            _interaction.WriteError("Could not detect the worker runtime for this project.");
            _interaction.WriteHint("Ensure the project contains the expected project files (e.g., .csproj, package.json).");
            return Task.FromResult(1);
        }

        _interaction.WriteError($"No pack provider for runtime '{detectedRuntime}'.");
        _interaction.WriteLine(l => l
            .Muted("Install the workload: ")
            .Command($"func workload install {detectedRuntime}"));
        return Task.FromResult(1);
    }
}
