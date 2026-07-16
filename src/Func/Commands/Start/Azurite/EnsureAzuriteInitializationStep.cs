// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Drives the managed-Azurite orchestrator inside the start initialization
/// pipeline. Sits between <c>prepare_host_run</c> (which materializes the
/// effective <c>AzureWebJobsStorage</c> value) and <c>start_host</c> (which
/// requires Azurite to be reachable for local storage configurations).
/// </summary>
internal sealed class EnsureAzuriteInitializationStep(
    IManagedAzuriteOrchestrator orchestrator,
    IProcessEnvironment processEnvironment) : FuncStartInitializationStep
{
    public const string StepId = "ensure_azurite";
    private const string AzureWebJobsStorageName = "AzureWebJobsStorage";

    private readonly IManagedAzuriteOrchestrator _orchestrator = orchestrator
        ?? throw new ArgumentNullException(nameof(orchestrator));

    private readonly IProcessEnvironment _processEnvironment = processEnvironment
        ?? throw new ArgumentNullException(nameof(processEnvironment));

    public override string Id => StepId;

    public override string Title => "Ensure Azurite";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        FunctionsProject project = context.State.Project
            ?? throw new InvalidOperationException("Functions project was not resolved.");
        FunctionsProjectHostRunContext hostRunContext = context.State.HostRunContext
            ?? throw new InvalidOperationException("Host run context was not prepared.");

        string? connectionString = ResolveStorageConnectionString(hostRunContext);

        ManagedAzuriteRequest request = new(
            StorageConnectionString: connectionString,
            ProjectRoot: project.WorkingDirectory.Info.FullName,
            Disabled: context.Options.NoAzurite,
            StartupTimeout: ManagedAzuriteRequest.DefaultStartupTimeout);

        // Forward orchestrator phase messages through the step's progress
        // channel so dashboards can surface them as sub-status while the
        // long-running probe / locate / launch / poll phases are in flight.
        // Reports are fire-and-forget; ordering is loose and the renderer
        // shows the latest message, which is exactly the desired behaviour.
        IProgress<string> progress = new CallbackProgress<string>(message =>
            _ = context.ReportProgressAsync(percent: 0, message, cancellationToken));

        ManagedAzuriteResult result = await _orchestrator.EnsureReadyAsync(request, progress, cancellationToken);

        switch (result)
        {
            case ManagedAzuriteResult.Disabled disabled:
                return StartInitializationStepResult.Completed(disabled.Reason);

            case ManagedAzuriteResult.UserManaged userManaged:
                return StartInitializationStepResult.Completed(userManaged.Reason);

            case ManagedAzuriteResult.Started started:
                context.State.ManagedAzurite = ManagedAzuriteHandle.Owning(started.Process, started.Mode);
                return StartInitializationStepResult.Completed(
                    $"Started managed Azurite ({started.Mode}). Data directory: {started.Paths.DataDirectory}");

            case ManagedAzuriteResult.Failed failed:
                throw new GracefulException(failed.UserMessage, isUserError: true, verboseMessage: failed.VerboseDetail);

            default:
                throw new InvalidOperationException($"Unknown ManagedAzuriteResult: {result.GetType().Name}");
        }
    }

    private string? ResolveStorageConnectionString(FunctionsProjectHostRunContext hostRunContext)
    {
        // Process env wins over local.settings.json by design (see
        // PrepareProjectHostRunInitializationStep). The prepare step does not
        // copy values that already exist in process env into the host run
        // context, so we have to check both.
        string? fromProcess = _processEnvironment.Get(AzureWebJobsStorageName);
        if (!string.IsNullOrWhiteSpace(fromProcess))
        {
            return fromProcess;
        }

        return hostRunContext.EnvironmentVariables.TryGetValue(AzureWebJobsStorageName, out string? value)
            ? value
            : null;
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        private readonly Action<T> _callback = callback ?? throw new ArgumentNullException(nameof(callback));

        public void Report(T value) => _callback(value);
    }
}
