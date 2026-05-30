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
    IInteractionService interaction) : FuncStartInitializationStep
{
    public const string StepId = "prepare_host_run";

    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider
        ?? throw new ArgumentNullException(nameof(localSettingsProvider));

    private readonly IProcessEnvironment _processEnvironment = processEnvironment
        ?? throw new ArgumentNullException(nameof(processEnvironment));

    private readonly IInteractionService _interaction = interaction
        ?? throw new ArgumentNullException(nameof(interaction));

    public override string Id => StepId;

    public override string Title => "Prepare project";

    public override async Task<StartInitializationStepResult> ExecuteAsync(StartInitializationStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

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

