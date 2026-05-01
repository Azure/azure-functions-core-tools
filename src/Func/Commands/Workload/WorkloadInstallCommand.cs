// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload install</c>. Installs a workload from a local extracted
/// .nupkg directory. NuGet feed acquisition lands in a follow-up; this
/// command is the install pipeline's entry point and is wired so that
/// follow-up only needs to resolve a package id to a directory and call
/// the same <see cref="IWorkloadInstaller"/>.
/// </summary>
internal sealed class WorkloadInstallCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;
    private readonly Option<DirectoryInfo> _fromOption;

    public WorkloadInstallCommand(IInteractionService interaction, IWorkloadInstaller installer)
        : base("install", "Install a workload from an already-extracted package directory.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));

        _fromOption = new Option<DirectoryInfo>("--from")
        {
            Description = "Path to a directory containing an extracted workload .nupkg (the .nuspec must be at the top level).",
            Required = true,
        };
        Options.Add(_fromOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var from = parseResult.GetValue(_fromOption)
            ?? throw new GracefulException("--from is required.", isUserError: true);

        var installed = await _installer.InstallFromDirectoryAsync(from.FullName, cancellationToken)
            .ConfigureAwait(false);

        _interaction.WriteSuccess(
            $"Installed workload '{installed.PackageId}' version '{installed.Version}'.");
        return 0;
    }
}
