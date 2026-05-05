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

    public Argument<string> PackageIdArgument { get; } = new("packageId")
    {
        Description = "Package ID (or alias) of the workload to install.",
    };

    public Option<string?> VersionOption { get; } = new("--version")
    {
        Description = "Specific version to install. Defaults to the latest available.",
    };

    public Option<DirectoryInfo?> FromOption { get; } = new("--from")
    {
        Description = "Path to a directory containing an extracted workload .nupkg (the .nuspec must be at the top level). Use for local development before publishing to a feed.",
    };

    public WorkloadInstallCommand(IInteractionService interaction, IWorkloadInstaller installer)
        : base("install", "Install a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));

        Arguments.Add(PackageIdArgument);
        Options.Add(VersionOption);
        Options.Add(FromOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var packageId = parseResult.GetValue(PackageIdArgument)
            ?? throw new GracefulException("packageId is required.", isUserError: true);
        var version = parseResult.GetValue(VersionOption);
        var from = parseResult.GetValue(FromOption);

        if (from is null)
        {
            // Feed-based acquisition (resolve packageId + optional version
            // against a NuGet feed, download, extract, then install) lands in
            // a follow-up. For now, --from is the only supported path.
            throw new GracefulException(
                $"Acquiring '{packageId}' from a NuGet feed is not yet supported. " +
                "Pass --from <path> with an extracted .nupkg directory.",
                isUserError: true);
        }

        var installed = await _installer.InstallFromDirectoryAsync(from.FullName, cancellationToken);

        if (!string.Equals(installed.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
        {
            _interaction.WriteHint(
                $"Note: --from package id '{installed.PackageId}' differs from requested id '{packageId}'.");
        }

        if (!string.IsNullOrEmpty(version)
            && !string.Equals(installed.Version, version, StringComparison.Ordinal))
        {
            _interaction.WriteHint(
                $"Note: --from version '{installed.Version}' differs from requested version '{version}'.");
        }

        _interaction.WriteSuccess(
            $"Installed workload '{installed.PackageId}' version '{installed.Version}'.");
        return 0;
    }
}
