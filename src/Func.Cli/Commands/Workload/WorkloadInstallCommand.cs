// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload install &lt;source&gt;</c>. Installs a workload from a
/// local <c>.nupkg</c> or directory. NuGet feed resolution is not yet wired
/// up — callers point at a built artifact directly.
/// </summary>
public class WorkloadInstallCommand : BaseCommand
{
    public static readonly Argument<string> SourceArgument = new("source")
    {
        Description = "Path to a workload .nupkg file or a directory containing workload.json",
        Arity = ArgumentArity.ExactlyOne,
    };

    private readonly IInteractionService _interaction;

    public WorkloadInstallCommand(IInteractionService interaction)
        : base("install", "Install a workload from a local package or directory.")
    {
        _interaction = interaction;
        Arguments.Add(SourceArgument);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var source = parseResult.GetValue(SourceArgument)!;

        var result = WorkloadInstaller.Install(source);

        var verb = result.Replaced ? "Updated" : "Installed";
        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted($"{verb} ")
            .Code(result.Entry.PackageId)
            .Muted(" ")
            .Code(result.Entry.Version)
            .Muted("."));
        _interaction.WriteHint($"Install path: {result.Entry.InstallPath}");

        return Task.FromResult(0);
    }
}
