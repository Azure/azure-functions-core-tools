// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Installs the resolved host workload.
/// </summary>
internal sealed class InstallHostWorkloadInitializationStep(
    IWorkloadInstaller installer,
    IWorkloadPaths workloadPaths,
    string? packageId,
    string hostVersion) : FuncStartInitializationStep
{
    public const string StepId = "install_host_workload";

    private readonly IWorkloadInstaller _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    private readonly IWorkloadPaths _workloadPaths = workloadPaths ?? throw new ArgumentNullException(nameof(workloadPaths));
    private readonly string _packageId = string.IsNullOrWhiteSpace(packageId) ? HostWorkloadPackage.CurrentPackageId : packageId;

    private readonly NuGetVersion _hostVersion = NuGetVersion.TryParse(hostVersion, out NuGetVersion? parsedHostVersion)
        ? parsedHostVersion
        : throw new ArgumentException("Host version must be a valid NuGet version.", nameof(hostVersion));

    public override string Id => StepId;

    public override string Title => "Install host workload";

    public override string Detail => $"Azure Functions host {_hostVersion.ToNormalizedString()}";

    public override StartInitializationDisplayKind DisplayKind => StartInitializationDisplayKind.Progress;

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.ReportProgressAsync(0, "Installing host workload", cancellationToken);

        WorkloadInstallResult result;
        try
        {
            result = await _installer.InstallFromCatalogAsync(
                _packageId,
                _hostVersion,
                source: null,
                includePrerelease: false,
                exact: true,
                force: false,
                progress: null,
                cancellationToken);
        }
        catch (WorkloadPackageNotFoundException ex)
        {
            throw CreateUserError(ex);
        }
        catch (AmbiguousPackageMatchException ex)
        {
            throw CreateUserError(ex);
        }
        catch (InvalidWorkloadException ex)
        {
            throw CreateUserError(ex);
        }
        catch (FileNotFoundException ex)
        {
            throw CreateUserError(ex);
        }
        catch (InvalidOperationException ex)
        {
            string message = $"{ex.Message} Run 'func workload install {HostWorkloadPackage.CurrentPackageId} --exact --force' to repair the install.";
            throw new GracefulException(message, isUserError: true, verboseMessage: ex.ToString());
        }

        context.State.HostVersion = result.Entry.PackageVersion;
        context.State.HostWorkload = CreateContentWorkloadInfo(result.Entry);
        string completionMessage = result.AlreadyInstalled
            ? $"Host {result.Entry.PackageVersion} already installed"
            : $"Installed host {result.Entry.PackageVersion}";

        // TODO: progress reporting here should show the download progress.
        // update this once the installer supports download progress reporting.
        await context.ReportProgressAsync(100, completionMessage, cancellationToken);

        return StartInitializationStepResult.Completed(completionMessage);
    }

    private static GracefulException CreateUserError(Exception exception)
        => new(exception.Message, isUserError: true, verboseMessage: exception.ToString());

    private ContentWorkloadInfo CreateContentWorkloadInfo(WorkloadEntry entry)
    {
        string installDirectory = _workloadPaths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        return new ContentWorkloadInfo(
            entry.PackageId,
            entry.PackageVersion,
            entry.Aliases,
            installDirectory,
            Path.GetFullPath(Path.Combine(installDirectory, "tools", "any")),
            string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.PackageId : entry.DisplayName,
            entry.Description ?? string.Empty);
    }
}
