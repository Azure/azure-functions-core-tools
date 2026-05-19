// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Resolves a directory as a Python Functions project when <c>host.json</c> is present
/// alongside <c>function_app.py</c>, a known dependency manifest (<c>requirements.txt</c>,
/// <c>pyproject.toml</c>, <c>uv.lock</c>, <c>poetry.lock</c>), or any <c>*.py</c> file at the project root.
/// </summary>
internal sealed class PythonProjectResolver : IProjectResolver
{
    private const string WorkerRuntime = "python";

    public Task<EvaluationResult> EvaluateAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!workingDirectory.Exists || !File.Exists(Path.Combine(workingDirectory.FullName, "host.json")))
        {
            return Task.FromResult(EvaluationResult.NoMatch("no host.json"));
        }

        // v2 programming model entry point: strongest signal.
        if (File.Exists(Path.Combine(workingDirectory.FullName, "function_app.py")))
        {
            return Task.FromResult(EvaluationResult.Match("found function_app.py", WorkerRuntime));
        }

        // Dependency-manager manifests. Lock files are checked alongside
        // pyproject.toml so a uv- or poetry-managed project without
        // requirements.txt is still claimed.
        string? manifest = FirstExisting(workingDirectory, "requirements.txt", "pyproject.toml", "uv.lock", "poetry.lock");
        if (manifest is not null)
        {
            return Task.FromResult(EvaluationResult.Match($"found {manifest}", WorkerRuntime));
        }

        // Fallback: any *.py at the project root.
        if (Directory.EnumerateFiles(workingDirectory.FullName, "*.py", SearchOption.TopDirectoryOnly).Any())
        {
            return Task.FromResult(EvaluationResult.Match("found *.py file", WorkerRuntime));
        }

        return Task.FromResult(EvaluationResult.NoMatch("host.json present but no Python fingerprint file"));
    }

    private static string? FirstExisting(DirectoryInfo directory, params string[] fileNames)
    {
        foreach (string name in fileNames)
        {
            if (File.Exists(Path.Combine(directory.FullName, name)))
            {
                return name;
            }
        }

        return null;
    }

    public Task<RuntimeStackInfo> GetRuntimeStackInfoAsync(WorkingDirectory workingDirectory, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
