// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
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
    IWorkloadInstaller workloadInstaller, IBasicTemplateLifecycle templates, IFileSystem fileSystem, IWorkloadOperationCoordinator coordinator,
    IWorkloadDeploymentService? nativeDeployment = null)
{
    private readonly IBasicCompanionCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly BasicAcquisitionPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly IPackageSourceProvider _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
    private readonly IWorkloadStore _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IWorkloadPaths _workloadPaths = workloadPaths ?? throw new ArgumentNullException(nameof(workloadPaths));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));
    private readonly IBasicTemplateLifecycle _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IWorkloadOperationCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly IWorkloadDeploymentService? _nativeDeployment = nativeDeployment;

    public async Task<BasicAcquisitionResult> RunAsync(BasicAcquisitionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StackPackageId);
        ArgumentNullException.ThrowIfNull(request.StackVersion);
        ArgumentNullException.ThrowIfNull(request.Directory);
        List<BasicAcquisitionStep> completed = [];
        string? revision = null;
        string? source = null;
        List<PreparedBasicTemplate> preparedTemplates = [];
        PreparedWorkloadInstall? preparedStack = null;
        PreparedWorkloadReuse? nativeStack = null;
        bool workloadRecoveryRequired = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            BasicCompanionManifest manifest = await _catalog.LoadAsync(cancellationToken);
            revision = manifest.Revision;
            IReadOnlyList<WorkloadEntry> installed = await _workloads.GetWorkloadsAsync(cancellationToken);
            try
            {
                string resolvedSource = _sourceProvider.GetSource(request.Source).Source;
                source = request.NativeContext is null ? resolvedSource : BasicCompanionManifestReader.Source(resolvedSource);
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
            if (request.NativeContext is not null)
            {
                WorkloadEntry selectedStack = RequireNativeStack(installed, request);
                IWorkloadDeploymentService deployment = _nativeDeployment
                    ?? throw new InvalidOperationException("Native acquisition requires the selected-stack deployment validator.");
                try
                {
                    nativeStack = await deployment.PrepareReuseAsync(selectedStack, cancellationToken, checkOnly: true);
                }
                catch (InvalidWorkloadException exception)
                {
                    throw NativeStackFailure(exception);
                }
            }
            TemplateEngineContext context = new(request.Directory)
            {
                RequireCleanStore = request.Check || request.NativeContext is not null,
            };
            List<BasicTemplateSelection> selected = [];
            foreach (BasicCompanionPlan companion in plan.Companions)
            {
                selected.Add(await _templates.SelectAsync(companion, context, cancellationToken));
            }
            if (request.NativeContext is not null)
            {
                ValidateNativeIdentities(selected);
            }

            WorkloadEntry? stack = FindStack(installed, request);
            bool stackInstalled = stack is not null && _fileSystem.DirectoryExists(_workloadPaths.GetInstallDirectory(stack.PackageId, stack.PackageVersion));
            if (request.Check)
            {
                if (nativeStack is not null)
                {
                    return await CheckNativeAsync(manifest, request, nativeStack, context, source, cancellationToken);
                }
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
                    preparedStack = await _workloadInstaller.PrepareFromCatalogAsync(request.StackPackageId, request.StackVersion, source,
                        request.IncludePrerelease, exact: true, force: false, progress: null, cancellationToken);
                }
                catch (Exception exception) when (exception is WorkloadPackageNotFoundException or InvalidWorkloadException or IOException
                    || (exception is InvalidOperationException && exception is not WorkloadPlanChangedException
                        && exception is not WorkloadRecoveryRequiredException)
                    || exception is HttpRequestException or NuGet.Protocol.Core.Types.FatalProtocolException
                    || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed,
                        $"Stack '{request.StackPackageId}' {request.StackVersion} installation from '{source}' failed: {exception.Message}", exception);
                }
            }

            foreach (BasicTemplateSelection selection in selected)
            {
                preparedTemplates.Add(await _templates.PrepareAsync(selection, context, request.Offline, cancellationToken));
            }

            // All preview reads are disposed and all network/private preflight is finished before writer admission.
            using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
            operation.Validate(_workloadPaths);
            installed = await _workloads.GetWorkloadsAsync(cancellationToken);
            if (nativeStack is not null)
            {
                await ValidateNativeStackAsync(operation, nativeStack, request, installed, cancellationToken);
            }
            BasicAcquisitionPlan currentPlan = _planner.Plan(manifest, request, installed, source)
                ?? throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged, "The prepared mapping no longer applies. Re-run explicit repair.");
            foreach (BasicTemplateSelection selection in selected)
            {
                BasicCompanionPlan companion = currentPlan.Companions.Single(c =>
                    string.Equals(c.PackageId, selection.Artifact.PackageId, StringComparison.OrdinalIgnoreCase));
                if (!companion.Candidates.Contains(selection.Artifact))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                        "Retained requirements now require different qualified bytes. Re-run explicit repair; no replacement was downloaded under the lease.");
                }

                BasicTemplateSelection actual = await _templates.SelectAsync(companion, context, cancellationToken);
                if (actual.Artifact != selection.Artifact || (selection.Installed && !actual.Installed))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                        "Companion content or ownership changed after preparation. Re-run explicit repair.");
                }
            }

            foreach (PreparedBasicTemplate prepared in preparedTemplates)
            {
                await _templates.RevalidatePreparedAsync(prepared, context, cancellationToken);
            }

            stack = FindStack(installed, request);
            if (preparedStack is not null)
            {
                try
                {
                    await _workloadInstaller.CommitPreparedAsync(operation, preparedStack, progress: null, cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidWorkloadException or IOException
                    || (exception is InvalidOperationException && exception is not WorkloadPlanChangedException
                        && exception is not WorkloadRecoveryRequiredException))
                {
                    throw new BasicAcquisitionException(operation.IsDirty ? BasicAcquisitionFailure.RecoveryRequired : BasicAcquisitionFailure.InstallFailed,
                        $"Stack installation failed; inspect workload recovery diagnostics before retry: {exception.Message}", exception);
                }
                finally
                {
                    workloadRecoveryRequired = operation.IsDirty;
                }
                stack = FindStack(await _workloads.GetWorkloadsAsync(cancellationToken), request);
            }

            if (stack is null || !_fileSystem.DirectoryExists(_workloadPaths.GetInstallDirectory(stack.PackageId, stack.PackageVersion)))
            {
                throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged, "The requested stack changed or was not persisted. Re-run explicit repair.");
            }

            completed.Add(new(request.StackPackageId, request.StackVersion.ToNormalizedString(), stackInstalled));
            foreach (PreparedBasicTemplate prepared in preparedTemplates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool reused;
                try
                {
                    reused = await _templates.CommitAsync(prepared, context, cancellationToken);
                }
                catch (IOException exception)
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed,
                        $"Companion/store operation failed; inspect its recovery diagnostics before retry: {exception.Message}", exception);
                }
                completed.Add(new(prepared.Artifact.PackageId, prepared.Artifact.Version.ToNormalizedString(), reused));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new(BasicAcquisitionStatus.Completed, revision, source, completed.AsReadOnly());
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            if (!workloadRecoveryRequired)
            {
                await IncludeRetainedStackAsync(request, completed);
            }
            return new(BasicAcquisitionStatus.Canceled, revision, source, completed.AsReadOnly(),
                Failure: workloadRecoveryRequired ? BasicAcquisitionFailure.RecoveryRequired : null,
                Message: workloadRecoveryRequired
                    ? "Acquisition canceled during workload mutation. Reconcile the workload dirty intent before retry; no unverified stack completion is reported."
                    : "Acquisition canceled. Completed packages remain; retry explicit repair. Active template mutation uses its owning store's recovery.",
                Cause: exception);
        }
        catch (BasicAcquisitionException exception)
        {
            if (!workloadRecoveryRequired)
            {
                await IncludeRetainedStackAsync(request, completed);
            }
            BasicAcquisitionStatus status = exception.Failure == BasicAcquisitionFailure.MissingMapping
                ? BasicAcquisitionStatus.Unavailable
                : completed.Count > 0 ? BasicAcquisitionStatus.PartialFailure : BasicAcquisitionStatus.Rejected;
            return new(status, revision, source, completed.AsReadOnly(), exception.Failure, exception.Message, exception.InnerException ?? exception);
        }
        catch (WorkloadRecoveryRequiredException exception)
        {
            return new(BasicAcquisitionStatus.Rejected, revision, source, completed.AsReadOnly(),
                BasicAcquisitionFailure.RecoveryRequired, exception.Message, exception);
        }
        catch (TemplateStoreRecoveryRequiredException exception)
        {
            return new(BasicAcquisitionStatus.Rejected, revision, source, completed.AsReadOnly(),
                BasicAcquisitionFailure.RecoveryRequired, exception.Message, exception);
        }
        catch (WorkloadPlanChangedException exception)
        {
            return new(BasicAcquisitionStatus.Rejected, revision, source, completed.AsReadOnly(),
                BasicAcquisitionFailure.PlanChanged, exception.Message, exception);
        }
        finally
        {
            preparedStack?.Dispose();
            nativeStack?.Dispose();
            foreach (PreparedBasicTemplate prepared in preparedTemplates)
            {
                prepared.Dispose();
            }
        }
    }

    private static void ValidateNativeIdentities(IReadOnlyList<BasicTemplateSelection> selected)
    {
        Dictionary<string, string> owners = new(StringComparer.Ordinal);
        foreach (BasicTemplateSelection selection in selected)
        {
            foreach (BasicTemplateDefinition template in selection.Artifact.Templates)
            {
                if (owners.TryGetValue(template.Identity, out string? owner)
                    && !string.Equals(owner, selection.Artifact.PackageId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidMapping,
                        $"Required native packages '{owner}' and '{selection.Artifact.PackageId}' claim the same template identity '{template.Identity}'. No package was acquired.");
                }
                owners[template.Identity] = selection.Artifact.PackageId;
            }
        }
    }

    private async Task<BasicAcquisitionResult> CheckNativeAsync(BasicCompanionManifest manifest, BasicAcquisitionRequest request,
        PreparedWorkloadReuse preparedStack, TemplateEngineContext context, string source, CancellationToken cancellationToken)
    {
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        operation.Validate(_workloadPaths);
        IReadOnlyList<WorkloadEntry> installed = await _workloads.GetWorkloadsAsync(cancellationToken);
        await ValidateNativeStackAsync(operation, preparedStack, request, installed, cancellationToken);
        BasicAcquisitionPlan plan = _planner.Plan(manifest, request, installed, source)
            ?? throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged, "The required native association no longer applies.");
        List<BasicAcquisitionStep> ready = [new(request.StackPackageId, request.StackVersion.ToNormalizedString(), true)];
        bool complete = true;
        foreach (BasicCompanionPlan companion in plan.Companions)
        {
            BasicTemplateSelection actual = await _templates.SelectAsync(companion, context, cancellationToken);
            complete &= actual.Installed;
            if (actual.Installed)
            {
                ready.Add(new(actual.Artifact.PackageId, actual.Artifact.Version.ToNormalizedString(), true));
            }
        }
        return new(complete ? BasicAcquisitionStatus.Completed : BasicAcquisitionStatus.Incomplete,
            plan.Revision, plan.Source, ready.AsReadOnly(),
            Message: "Check mode verifies required native roles without acquiring payloads, repairing packages or recovering retained state.");
    }

    private async Task ValidateNativeStackAsync(IWorkloadOperation operation, PreparedWorkloadReuse prepared,
        BasicAcquisitionRequest request, IReadOnlyList<WorkloadEntry> installed, CancellationToken cancellationToken)
    {
        RequireNativeStack(installed, request);
        try
        {
            await _nativeDeployment!.ValidateReuseAsync(operation, prepared, cancellationToken, checkOnly: true);
        }
        catch (InvalidWorkloadException exception)
        {
            throw NativeStackFailure(exception);
        }
    }

    private static WorkloadEntry RequireNativeStack(IReadOnlyList<WorkloadEntry> installed, BasicAcquisitionRequest request)
    {
        WorkloadEntry? actual = FindStack(installed, request);
        WorkloadEntry selected = request.NativeContext!.SelectedStack;
        if (actual is null || !WorkloadStateSnapshot.Same(actual, selected))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                "The selected stack owner, version or source changed. Run setup again; no external package was admitted.");
        }
        if (actual.Kind != WorkloadKind.Workload || actual.EntryPoint is null)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InstalledConflict,
                "The selected native template association requires an executable installed stack, not a content or catalog row.");
        }
        return actual;
    }

    private static BasicAcquisitionException NativeStackFailure(InvalidWorkloadException exception)
        => new(exception.InnerException is TemplateStoreRecoveryRequiredException
                ? BasicAcquisitionFailure.RecoveryRequired : BasicAcquisitionFailure.InstalledConflict,
            exception.Message, exception);

    private async Task IncludeRetainedStackAsync(BasicAcquisitionRequest request, List<BasicAcquisitionStep> completed)
    {
        if (request.NativeContext is not null
            || completed.Any(step => string.Equals(step.PackageId, request.StackPackageId, StringComparison.OrdinalIgnoreCase)))
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
    {
        if (request.NativeContext is { } native)
        {
            WorkloadEntry[] matches = [.. installed.Where(entry =>
                string.Equals(entry.PackageId, native.SelectedStack.PackageId, StringComparison.OrdinalIgnoreCase)
                && entry.PackageVersion == native.SelectedStack.PackageVersion)];
            return matches.Length == 1 ? matches[0] : null;
        }
        return installed.FirstOrDefault(entry =>
            string.Equals(entry.LogicalPackage?.PackageId ?? entry.PackageId, request.StackPackageId, StringComparison.OrdinalIgnoreCase)
            && NuGetVersion.TryParse(entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion, out NuGetVersion? version)
            && version == request.StackVersion);
    }
}
