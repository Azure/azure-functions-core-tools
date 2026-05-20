// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Mutable host run state prepared by a Functions project before host startup.
/// </summary>
public sealed class FunctionsProjectHostRunContext
{
    public const string WorkerRuntimeEnvironmentVariable = "FUNCTIONS_WORKER_RUNTIME";

    private DirectoryInfo _startupDirectory;

    public FunctionsProjectHostRunContext(
        DirectoryInfo startupDirectory,
        string workerRuntime,
        IDictionary<string, string> environmentVariables)
    {
        ArgumentNullException.ThrowIfNull(startupDirectory);
        ArgumentNullException.ThrowIfNull(environmentVariables);

        _startupDirectory = startupDirectory;
        EnvironmentVariables = environmentVariables;
        WorkerRuntime = workerRuntime;
    }

    public DirectoryInfo StartupDirectory
    {
        get => _startupDirectory;
        set => _startupDirectory = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IDictionary<string, string> EnvironmentVariables { get; }

    public string WorkerRuntime
    {
        get
        {
            if (!EnvironmentVariables.TryGetValue(WorkerRuntimeEnvironmentVariable, out string? workerRuntime)
                || string.IsNullOrWhiteSpace(workerRuntime))
            {
                throw new InvalidOperationException($"{WorkerRuntimeEnvironmentVariable} is required.");
            }

            return workerRuntime;
        }

        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            EnvironmentVariables[WorkerRuntimeEnvironmentVariable] = value;
        }
    }
}
