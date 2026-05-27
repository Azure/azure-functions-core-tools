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
        IDictionary<string, string> environmentVariables,
        bool skipBuild = false)
    {
        ArgumentNullException.ThrowIfNull(startupDirectory);
        ArgumentNullException.ThrowIfNull(environmentVariables);

        _startupDirectory = startupDirectory;
        EnvironmentVariables = environmentVariables;
        SkipBuild = skipBuild;
        WorkerRuntime = workerRuntime;
        SkipBuild = skipBuild;
    }

    public DirectoryInfo StartupDirectory
    {
        get => _startupDirectory;
        set => _startupDirectory = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IDictionary<string, string> EnvironmentVariables { get; }

    /// <summary>
    /// When <c>true</c>, project pre-run hooks should skip optional build/restore steps
    /// (e.g. <c>npm run build</c>, <c>go build</c>) but still perform dependency
    /// installs required for the host to start. Matches the contract of the
    /// <c>func start --no-build</c> flag.
    /// </summary>
    public bool SkipBuild { get; }

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
