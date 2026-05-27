// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Lets the resolved project prepare host process state before startup.
/// Populates the environment dictionary the host will see: <c>local.settings.json</c> values,
/// <c>FUNCTIONS_WORKER_RUNTIME</c>, the worker directory hint, and any bundle env vars.
/// </summary>
internal sealed class PrepareProjectHostRunInitializationStep(ILocalSettingsProvider localSettingsProvider) : DemoInitializationStep
{
    public const string StepId = "prepare_host_run";

    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider
        ?? throw new ArgumentNullException(nameof(localSettingsProvider));

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

        await project.PrepareForHostRunAsync(hostRunContext, cancellationToken);

        context.State.HostRunContext = hostRunContext;

        return StartInitializationStepResult.Completed(hostRunContext.StartupDirectory.FullName);
    }

    private Dictionary<string, string> BuildEnvironmentVariables(StartInitializationStepContext context, FunctionsProject project, IFunctionsWorker worker)
    {
        // Priority (low -> high): local.settings.json Values, host state, worker hints, bundle vars.
        // Bundle vars and worker hints sit on top because the CLI just resolved them and should
        // win over anything stale a user left in local.settings.json. In particular, the resolved
        // FUNCTIONS_WORKER_RUNTIME overrides whatever local.settings.json declares: the project
        // resolver picked a worker, and letting a stale value steer the host would surface as a
        // confusing "wrong language" failure deep in startup rather than a clear resolver error.
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        LocalSettingsSnapshot localSettings = _localSettingsProvider.Get(project.WorkingDirectory.Info);
        foreach ((string name, string value) in localSettings.Values)
        {
            environmentVariables[name] = value;
        }

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
            string key = $"languageWorkers:{worker.WorkerRuntime}:workerDirectory";
            environmentVariables[key] = workerDirectory;
        }

        foreach ((string name, string value) in context.State.BundleEnvVarsForHost)
        {
            environmentVariables[name] = value;
        }

        return environmentVariables;
    }
}
