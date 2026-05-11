// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload install &lt;package&gt;</c>. Installs a workload from a
/// <c>.nupkg</c> on disk. Feed-based acquisition is not yet supported.
/// </summary>
internal sealed class WorkloadInstallCommand : FuncCliCommand
{
    private const string NupkgExtension = ".nupkg";

    private readonly IInteractionService _interaction;
    private readonly IWorkloadInstaller _installer;

    public Argument<string> WorkloadArgument { get; } = new("id")
    {
        Description = "Workload to install. Currently must be a path to a .nupkg on disk.",
    };

    public Option<bool> ForceOption { get; } = new("--force", "-f")
    {
        Description = "Overwrite an existing install of the same id and version.",
    };

    public WorkloadInstallCommand(IInteractionService interaction, IWorkloadInstaller installer)
        : base("install", "Install a workload.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));

        WorkloadArgument.Validators.Add(result =>
        {
            string? value = result.GetValue(WorkloadArgument);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("A workload id is required.");
                return;
            }

            if (!value.EndsWith(NupkgExtension, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    $"Resolving '{value}' against a NuGet feed is not yet supported. " +
                    "Pass a path to a .nupkg on disk.");
            }
        });

        Arguments.Add(WorkloadArgument);
        Options.Add(ForceOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string workload = parseResult.GetValue(WorkloadArgument)!;
        bool force = parseResult.GetValue(ForceOption);

        try
        {
            WorkloadInstallResult result = await _installer.InstallFromPackageAsync(workload, force, cancellationToken);
            _interaction.WriteSuccess(BuildSuccessMessage(result));
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (InvalidWorkloadException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(
                $"{ex.Message} Pass --force to repair the install.",
                isUserError: true);
        }
    }

    private static string BuildSuccessMessage(WorkloadInstallResult result)
    {
        WorkloadEntry entry = result.Entry;
        string verb = result.AlreadyInstalled
            ? $"Workload '{entry.PackageId}' version '{entry.PackageVersion}' is already installed"
            : $"Installed workload '{entry.PackageId}' version '{entry.PackageVersion}'";

        return entry.Kind switch
        {
            WorkloadKind.Workload when entry.EntryPoint is not null
                => $"{verb} (entry point: {entry.EntryPoint.Type}).",
            WorkloadKind.Content
                => $"{verb} (content at '{entry.Source}').",
            _ => $"{verb}.",
        };
    }
}
