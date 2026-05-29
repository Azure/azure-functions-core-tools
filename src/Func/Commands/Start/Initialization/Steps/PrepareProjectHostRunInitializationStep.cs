// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Lets the resolved project prepare host process state before startup.
/// Populates the environment dictionary the host will see: <c>local.settings.json</c> values,
/// <c>FUNCTIONS_WORKER_RUNTIME</c>, the worker directory hint, and any bundle env vars.
/// </summary>
internal sealed class PrepareProjectHostRunInitializationStep(
    ILocalSettingsProvider localSettingsProvider,
    IProcessEnvironment processEnvironment,
    IInteractionService interaction,
    TimeProvider? timeProvider = null,
    TimeSpan? heartbeatInterval = null) : DemoInitializationStep
{
    public const string StepId = "prepare_host_run";

    // How often to emit a "still working" heartbeat while the underlying
    // project preparation runs. Long enough not to spam the dashboard,
    // short enough that the user can tell the CLI hasn't hung.
    internal static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(3);

    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider
        ?? throw new ArgumentNullException(nameof(localSettingsProvider));

    private readonly IProcessEnvironment _processEnvironment = processEnvironment
        ?? throw new ArgumentNullException(nameof(processEnvironment));

    private readonly IInteractionService _interaction = interaction
        ?? throw new ArgumentNullException(nameof(interaction));

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private readonly TimeSpan _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;

    public override string Id => StepId;

    public override string Title => "Prepare project";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SimulateWorkAsync(context, cancellationToken);

        FunctionsProject project = context.State.Project
            ?? throw new InvalidOperationException("Functions project was not resolved.");

        IFunctionsWorker worker = context.State.Worker
            ?? throw new InvalidOperationException("Functions worker was not resolved.");

        Dictionary<string, string> environmentVariables = BuildEnvironmentVariables(context, project, worker);

        var hostRunContext = new FunctionsProjectHostRunContext(
            project.WorkingDirectory.Info,
            worker.WorkerRuntime,
            environmentVariables,
            skipBuild: context.Options.NoBuild);

        await PrepareWithHeartbeatAsync(context, project, hostRunContext, cancellationToken);

        context.State.HostRunContext = hostRunContext;

        return StartInitializationStepResult.Completed(hostRunContext.StartupDirectory.FullName);
    }

    // The underlying PrepareForHostRunAsync is opaque (it shells out to `dotnet build`
    // for .NET projects, which can take 30s+ on a cold cache). Without a periodic
    // status update users can't tell the CLI from a hang. We tee a periodic
    // "still working (Ns elapsed)" message into the renderer's status slot while
    // the prepare task runs, then stop the timer in `finally` so a thrown
    // build error still surfaces normally.
    private async Task PrepareWithHeartbeatAsync(
        StartInitializationStepContext context,
        FunctionsProject project,
        FunctionsProjectHostRunContext hostRunContext,
        CancellationToken cancellationToken)
    {
        string initialMessage = BuildInitialMessage(project, hostRunContext.SkipBuild);
        long startTimestamp = _timeProvider.GetTimestamp();

        await context.ReportProgressAsync(percent: 0, message: initialMessage, cancellationToken);

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeatTask = RunHeartbeatAsync(context, initialMessage, startTimestamp, heartbeatCts.Token);

        try
        {
            await project.PrepareForHostRunAsync(hostRunContext, cancellationToken);
        }
        finally
        {
            heartbeatCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected: we cancel the heartbeat once the prepare task completes.
            }
        }
    }

    private async Task RunHeartbeatAsync(
        StartInitializationStepContext context,
        string baseMessage,
        long startTimestamp,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, _timeProvider, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            TimeSpan elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            string message = $"{baseMessage} ({(int)elapsed.TotalSeconds}s elapsed)";
            await context.ReportProgressAsync(percent: 0, message: message, cancellationToken);
        }
    }

    private static string BuildInitialMessage(FunctionsProject project, bool skipBuild)
    {
        string projectKind = project.GetType().Name;
        bool isDotNetSource = projectKind.Contains("DotNetSource", StringComparison.Ordinal);

        if (skipBuild)
        {
            return "Preparing project (build skipped)...";
        }

        return isDotNetSource
            ? "Building .NET project..."
            : "Preparing project...";
    }

    private Dictionary<string, string> BuildEnvironmentVariables(StartInitializationStepContext context, FunctionsProject project, IFunctionsWorker worker)
    {
        // Priority (low -> high): local.settings.json Values (gated by current process env),
        // host state, worker hints, bundle vars. A shell-set env var beats local.settings.json:
        // users override settings per-shell for CI, debugging, and secret rotation, and a stale
        // value in local.settings.json must not silently win. Bundle vars and worker hints sit
        // on top because the CLI just resolved them; in particular, the resolved
        // FUNCTIONS_WORKER_RUNTIME overrides whatever local.settings.json declares so a stale
        // value can't steer the host into a confusing "wrong language" failure deep in startup.
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        LocalSettingsSnapshot localSettings = _localSettingsProvider.Get(project.WorkingDirectory.Info);
        MergeLocalSettingsValues(localSettings.Values, environmentVariables);

        foreach ((string name, string value) in context.State.HostEnvironmentVariables)
        {
            environmentVariables[name] = value;
        }

        environmentVariables[FunctionsProjectHostRunContext.WorkerRuntimeEnvironmentVariable] = worker.WorkerRuntime;

        string? workerDirectory = Path.GetDirectoryName(worker.WorkerConfigPath);
        if (!string.IsNullOrEmpty(workerDirectory))
        {
            // The host scans `languageWorkers:<runtime>:workerDirectory` for a worker.config.json.
            // Pointing it at the resolved workload content folder lets installed workers light up
            // without copying assets into the project.
            string key = $"languageWorkers__{worker.WorkerRuntime}__workerDirectory";
            environmentVariables[key] = workerDirectory;
        }

        foreach ((string name, string value) in context.State.BundleEnvVarsForHost)
        {
            environmentVariables[name] = value;
        }

        return environmentVariables;
    }

    private void MergeLocalSettingsValues(IReadOnlyDictionary<string, string> values, Dictionary<string, string> environmentVariables)
    {
        foreach ((string name, string value) in values)
        {
            if (string.IsNullOrEmpty(name))
            {
                _interaction.WriteWarning("Skipping local setting with empty key.");
                continue;
            }

            if (_processEnvironment.Get(name) is not null)
            {
                _interaction.WriteWarning($"Skipping '{name}' from local.settings.json: already set in the current environment.");
                continue;
            }

            // Empty string is intentionally preserved: callers use it to clear an inherited value.
            environmentVariables[name] = value;
        }
    }
}

