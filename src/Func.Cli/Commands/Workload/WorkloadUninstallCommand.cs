// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload uninstall &lt;packageId&gt;</c>. Removes a workload
/// entry from the global manifest and (by default) deletes its install
/// directory.
/// </summary>
public class WorkloadUninstallCommand : BaseCommand
{
    public static readonly Argument<string> PackageIdArgument = new("packageId")
    {
        Description = "The workload package id to uninstall",
        Arity = ArgumentArity.ExactlyOne,
    };

    public static readonly Option<bool> KeepFilesOption = new("--keep-files")
    {
        Description = "Remove the manifest entry but keep the install directory on disk",
    };

    private readonly IInteractionService _interaction;

    public WorkloadUninstallCommand(IInteractionService interaction)
        : base("uninstall", "Uninstall a workload.")
    {
        _interaction = interaction;
        Arguments.Add(PackageIdArgument);
        Options.Add(KeepFilesOption);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var packageId = parseResult.GetValue(PackageIdArgument)!;
        var keepFiles = parseResult.GetValue(KeepFilesOption);

        var removed = WorkloadInstaller.Uninstall(packageId, deleteFiles: !keepFiles);
        if (!removed)
        {
            _interaction.WriteError($"Workload '{packageId}' is not installed.");
            return Task.FromResult(1);
        }

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Uninstalled ")
            .Code(packageId)
            .Muted("."));

        return Task.FromResult(0);
    }
}
