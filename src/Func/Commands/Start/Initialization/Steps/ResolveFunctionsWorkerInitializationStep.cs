// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the Functions worker required by the project.
/// </summary>
internal sealed class ResolveFunctionsWorkerInitializationStep(
    IFunctionsWorkerResolverFactory workerResolverFactory,
    IFunctionsWorkerInstaller workerInstaller) : DemoInitializationStep
{
    public const string StepId = "resolve_worker";

    private readonly IFunctionsWorkerResolverFactory _workerResolverFactory = workerResolverFactory
        ?? throw new ArgumentNullException(nameof(workerResolverFactory));

    private readonly IFunctionsWorkerInstaller _workerInstaller = workerInstaller ?? throw new ArgumentNullException(nameof(workerInstaller));

    public override string Id => StepId;

    public override string Title => "Resolve worker";

    public override StartInitializationDisplayKind DisplayKind => StartInitializationDisplayKind.Status;

    public override async Task<StartInitializationStepResult> ExecuteAsync(StartInitializationStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SimulateWorkAsync(context, cancellationToken);

        FunctionsProject project = context.State.Project ?? throw new InvalidOperationException("Functions project was not resolved.");
        IReadOnlyDictionary<string, VersionRange> workerVersionRanges =
            context.State.ResolvedProfile?.WorkerVersionRanges
            ?? new Dictionary<string, VersionRange>(StringComparer.OrdinalIgnoreCase);

        FunctionsWorkerResolutionResult result = await ResolveWorkerAsync(project, workerVersionRanges, cancellationToken);

        if (result is FunctionsWorkerResolutionResult.NotResolved notResolved
            && TryGetInstallableWorker(notResolved.Failure, out FunctionsWorkerId? workerId)
            && workerId is not null)
        {
            result = await TryInstallAndResolveWorkerAsync(context, workerId, workerVersionRanges, notResolved.Failure, cancellationToken);
        }

        if (result is not FunctionsWorkerResolutionResult.Resolved resolved)
        {
            var failedResult = (FunctionsWorkerResolutionResult.NotResolved)result;
            throw CreateWorkerResolutionException(failedResult.Failure, context);
        }

        ValidateSupportedRuntime(context, resolved.Worker);
        context.State.Worker = resolved.Worker;

        string completionMessage = string.IsNullOrWhiteSpace(resolved.Worker.Version)
            ? resolved.Worker.WorkerRuntime
            : $"{resolved.Worker.WorkerRuntime} {resolved.Worker.Version}";

        await context.ReportProgressAsync(100, $"Resolved worker {completionMessage}", cancellationToken);

        return StartInitializationStepResult.Completed(completionMessage);
    }

    private async Task<FunctionsWorkerResolutionResult> TryInstallAndResolveWorkerAsync(
        StartInitializationStepContext context,
        FunctionsWorkerId workerId,
        IReadOnlyDictionary<string, VersionRange> workerVersionRanges,
        FunctionsWorkerResolutionFailure failure,
        CancellationToken cancellationToken)
    {
        if (context.Options.Offline)
        {
            return FunctionsWorkerResolutionResults.NotResolved(failure);
        }

        if (!context.CanPrompt)
        {
            return FunctionsWorkerResolutionResults.NotResolved(failure);
        }

        string packageId = FunctionsWorkerWorkloadPackages.GetPackageId(workerId);
        await context.ReportProgressAsync(0, $"Installing worker workload {packageId}", cancellationToken);
        FunctionsWorkerInstallResult installResult = await InstallWorkerAsync(workerId, workerVersionRanges, cancellationToken);

        WorkloadInstallResult workloadInstallResult = installResult.WorkloadInstallResult;
        string completionMessage = workloadInstallResult.AlreadyInstalled
            ? $"Worker {workloadInstallResult.Entry.PackageVersion} already installed"
            : $"Installed worker {workloadInstallResult.Entry.PackageVersion}";
        await context.ReportProgressAsync(50, completionMessage, cancellationToken);

        return FunctionsWorkerResolutionResults.Resolved(installResult.Worker);
    }

    private async Task<FunctionsWorkerInstallResult> InstallWorkerAsync(
        FunctionsWorkerId workerId,
        IReadOnlyDictionary<string, VersionRange> workerVersionRanges,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _workerInstaller.InstallAsync(workerId, workerVersionRanges, cancellationToken);
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
            string message = $"{ex.Message} Run '{FunctionsWorkerWorkloadPackages.GetRepairCommand(workerId)}' to repair the install.";
            throw new GracefulException(message, isUserError: true, verboseMessage: ex.ToString());
        }
    }

    private async Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(
        FunctionsProject project,
        IReadOnlyDictionary<string, VersionRange> workerVersionRanges,
        CancellationToken cancellationToken)
    {
        IFunctionsWorkerResolver resolver = _workerResolverFactory.Create(workerVersionRanges);
        var resolutionContext = new FunctionsWorkerResolutionContext(resolver);
        return await project.WorkerReference.ResolveWorkerAsync(resolutionContext, cancellationToken);
    }

    private static bool TryGetInstallableWorker(FunctionsWorkerResolutionFailure failure, out FunctionsWorkerId? workerId)
    {
        switch (failure)
        {
            case FunctionsWorkerResolutionFailure.NotInstalled notInstalled:
                workerId = notInstalled.WorkerId;
                return true;
            case FunctionsWorkerResolutionFailure.MissingCompatibleVersion missingCompatibleVersion:
                workerId = missingCompatibleVersion.WorkerId;
                return true;
            default:
                workerId = null;
                return false;
        }
    }

    private static GracefulException CreateWorkerResolutionException(
        FunctionsWorkerResolutionFailure failure,
        StartInitializationStepContext context)
    {
        string message = failure switch
        {
            FunctionsWorkerResolutionFailure.NotInstalled notInstalled => CreateNotInstalledMessage(notInstalled.WorkerId, context),
            FunctionsWorkerResolutionFailure.MissingCompatibleVersion missingCompatibleVersion => CreateMissingCompatibleVersionMessage(missingCompatibleVersion, context),
            FunctionsWorkerResolutionFailure.InvalidInstallation invalidInstallation => CreateInvalidInstallationMessage(invalidInstallation),
            _ => failure.Message,
        };

        return new GracefulException(message, isUserError: true);
    }

    private static string CreateNotInstalledMessage(FunctionsWorkerId workerId, StartInitializationStepContext context)
    {
        string installCommand = FunctionsWorkerWorkloadPackages.GetInstallCommand(workerId);
        string message = $"The Functions worker workload for runtime '{workerId.Value}' is not installed. Run '{installCommand}' to install it.";
        return context.Options.Offline ? $"{message} Remove '--offline' to allow automatic installation." : message;
    }

    private static string CreateMissingCompatibleVersionMessage(
        FunctionsWorkerResolutionFailure.MissingCompatibleVersion failure,
        StartInitializationStepContext context)
    {
        if (failure.VersionConstraint is null)
        {
            string repairCommand = FunctionsWorkerWorkloadPackages.GetRepairCommand(failure.WorkerId);
            return $"Installed Functions worker workloads for runtime '{failure.WorkerId.Value}' do not include a valid package version. "
                + $"Run '{repairCommand}' to repair the install.";
        }

        string installCommand = FunctionsWorkerWorkloadPackages.GetInstallCommand(failure.WorkerId);
        string message = $"No installed Functions worker workload for runtime '{failure.WorkerId.Value}' satisfies '{failure.VersionConstraint}'. "
            + $"Run '{installCommand}' to install a compatible version.";
        return context.Options.Offline ? $"{message} Remove '--offline' to allow automatic installation." : message;
    }

    private static string CreateInvalidInstallationMessage(FunctionsWorkerResolutionFailure.InvalidInstallation failure)
    {
        string repairCommand = FunctionsWorkerWorkloadPackages.GetRepairCommand(failure.WorkerId);
        return $"The installed Functions worker workload '{failure.PackageId}' version '{failure.PackageVersion}' is invalid: "
            + $"{failure.Message} Run '{repairCommand}' to repair the install.";
    }

    private static void ValidateSupportedRuntime(StartInitializationStepContext context, IFunctionsWorker worker)
    {
        if (context.State.ResolvedProfile is not { SupportedRuntimes: { } supportedRuntimes } profile)
        {
            return;
        }

        if (supportedRuntimes.Any(runtime => string.Equals(runtime, worker.WorkerRuntime, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string message = $"Profile '{profile.Name}' does not support the detected runtime '{worker.WorkerRuntime}'. "
            + $"Supported runtimes: {string.Join(", ", supportedRuntimes)}.";
        throw new GracefulException(message, isUserError: true);
    }

    private static GracefulException CreateUserError(Exception exception)
        => new(exception.Message, isUserError: true, verboseMessage: exception.ToString());
}
