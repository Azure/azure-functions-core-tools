// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

/// <summary>
/// Stages explicit basic setup/repair without registering a public command or changing current setup policy.
/// </summary>
internal sealed class BasicAcquisitionRunner(IBasicCompanionCatalog catalog, BasicAcquisitionPlanner planner,
    IPackageSourceProvider sourceProvider, IWorkloadStore workloads, IWorkloadPaths workloadPaths,
    IWorkloadInstaller workloadInstaller, IBasicTemplateLifecycle templates, IFileSystem fileSystem)
{
    private readonly IBasicCompanionCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly BasicAcquisitionPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly IPackageSourceProvider _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
    private readonly IWorkloadStore _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IWorkloadPaths _workloadPaths = workloadPaths ?? throw new ArgumentNullException(nameof(workloadPaths));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));
    private readonly IBasicTemplateLifecycle _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<BasicAcquisitionResult> RunAsync(BasicAcquisitionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StackPackageId);
        ArgumentNullException.ThrowIfNull(request.StackVersion);
        ArgumentNullException.ThrowIfNull(request.Directory);
        List<BasicAcquisitionStep> completed = [];
        string? revision = null;
        string? source = null;
        bool acquisitionStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            BasicCompanionManifest manifest = await _catalog.LoadAsync(cancellationToken);
            revision = manifest.Revision;
            IReadOnlyList<WorkloadEntry> installed = await _workloads.GetWorkloadsAsync(cancellationToken);
            try
            {
                source = _sourceProvider.GetSource(request.Source).Source;
            }
            catch (ArgumentException exception)
            {
                throw new BasicAcquisitionException(BasicAcquisitionFailure.SourceConflict, exception.Message, exception);
            }
            BasicAcquisitionPlan? plan = _planner.Plan(manifest, request, installed, source);
            if (plan is null)
            {
                return new(BasicAcquisitionStatus.NotApplicable, revision, source, []);
            }

            source = plan.Source;
            TemplateEngineContext context = new(request.Directory);
            List<BasicTemplateSelection> selected = [];
            foreach (BasicCompanionPlan companion in plan.Companions)
            {
                selected.Add(await _templates.SelectAsync(companion, context, cancellationToken));
            }

            WorkloadEntry? stack = FindStack(installed, request);
            bool stackInstalled = stack is not null && _fileSystem.DirectoryExists(_workloadPaths.GetInstallDirectory(stack.PackageId, stack.PackageVersion));
            if (request.Check)
            {
                return new(stackInstalled && selected.All(selection => selection.Installed)
                    ? BasicAcquisitionStatus.Completed : BasicAcquisitionStatus.Incomplete, revision, source, [],
                    Message: "Check mode does not acquire payloads or repair packages.");
            }

            if (!stackInstalled)
            {
                if (request.Offline)
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.PayloadUnavailable,
                        "The requested stack is not installed. Offline repair cannot download it.");
                }

                try
                {
                    acquisitionStarted = true;
                    await _workloadInstaller.InstallFromCatalogAsync(request.StackPackageId, request.StackVersion, source,
                        request.IncludePrerelease, exact: true, force: false, cancellationToken: cancellationToken);
                }
                catch (Exception exception) when (exception is WorkloadPackageNotFoundException or InvalidWorkloadException or IOException or InvalidOperationException
                    or HttpRequestException or NuGet.Protocol.Core.Types.FatalProtocolException
                    || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed,
                        $"Stack '{request.StackPackageId}' {request.StackVersion} installation from '{source}' failed: {exception.Message}", exception);
                }

                // A returned result is not proof of persistence. Partial repair relies on the owning store's actual retained row.
                stack = FindStack(await _workloads.GetWorkloadsAsync(cancellationToken), request);
                if (stack is null || !_fileSystem.DirectoryExists(_workloadPaths.GetInstallDirectory(stack.PackageId, stack.PackageVersion)))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed, "The workload installer did not persist the requested stack.");
                }
            }

            completed.Add(new(request.StackPackageId, request.StackVersion.ToNormalizedString(), stackInstalled));
            acquisitionStarted = true;
            foreach (BasicTemplateSelection selection in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool reused;
                try
                {
                    reused = await _templates.EnsureAsync(selection.Artifact, context, request.Offline, cancellationToken);
                }
                catch (IOException exception)
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed,
                        $"Companion/store operation failed; inspect its recovery diagnostics before retry: {exception.Message}", exception);
                }
                completed.Add(new(selection.Artifact.PackageId, selection.Artifact.Version.ToNormalizedString(), reused));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new(BasicAcquisitionStatus.Completed, revision, source, completed.AsReadOnly());
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            await IncludeRetainedStackAsync(request, completed);
            return new(BasicAcquisitionStatus.Canceled, revision, source, completed.AsReadOnly(),
                Message: "Acquisition canceled. Completed packages remain; retry explicit repair. Active template mutation uses its owning store's recovery.",
                Cause: exception);
        }
        catch (BasicAcquisitionException exception)
        {
            await IncludeRetainedStackAsync(request, completed);
            BasicAcquisitionStatus status = exception.Failure == BasicAcquisitionFailure.MissingMapping
                ? BasicAcquisitionStatus.Unavailable
                : acquisitionStarted && completed.Count > 0 ? BasicAcquisitionStatus.PartialFailure : BasicAcquisitionStatus.Rejected;
            return new(status, revision, source, completed.AsReadOnly(), exception.Failure, exception.Message, exception.InnerException ?? exception);
        }
    }

    private async Task IncludeRetainedStackAsync(BasicAcquisitionRequest request, List<BasicAcquisitionStep> completed)
    {
        if (completed.Any(step => string.Equals(step.PackageId, request.StackPackageId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        WorkloadEntry? retained = FindStack(await _workloads.GetWorkloadsAsync(CancellationToken.None), request);
        if (retained is not null && _fileSystem.DirectoryExists(_workloadPaths.GetInstallDirectory(retained.PackageId, retained.PackageVersion)))
        {
            completed.Add(new(request.StackPackageId, request.StackVersion.ToNormalizedString(), true));
        }
    }

    private static WorkloadEntry? FindStack(IReadOnlyList<WorkloadEntry> installed, BasicAcquisitionRequest request)
        => installed.FirstOrDefault(entry =>
            string.Equals(entry.LogicalPackage?.PackageId ?? entry.PackageId, request.StackPackageId, StringComparison.OrdinalIgnoreCase)
            && NuGetVersion.TryParse(entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion, out NuGetVersion? version)
            && version == request.StackVersion);
}
