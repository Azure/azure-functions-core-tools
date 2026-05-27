// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using static Azure.Functions.Cli.Projects.ProjectCreationResults;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Creates Python Functions projects from Python-specific fingerprints.
/// </summary>
internal sealed class PythonProjectFactory : IFunctionsProjectFactory
{
    private static readonly FunctionsWorkerId _workerId = new("python");

    public Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo workingDirectory = context.WorkingDirectory.Info;
        if (!workingDirectory.Exists)
        {
            return Task.FromResult(NotCreated("directory does not exist"));
        }

        string? reason = TryGetReason(workingDirectory);
        if (reason is null)
        {
            return Task.FromResult(NotCreated("no Python project fingerprint found"));
        }

        FunctionsProject project = new PythonFunctionsProject(context.WorkingDirectory);
        return Task.FromResult(Created(project, reason));
    }

    private static string? TryGetReason(DirectoryInfo workingDirectory)
    {
        // v2 programming model entry point: strongest signal.
        if (File.Exists(Path.Combine(workingDirectory.FullName, "function_app.py")))
        {
            return "found function_app.py";
        }

        // Dependency-manager manifests. Lock files are checked alongside
        // pyproject.toml so a uv- or poetry-managed project without
        // requirements.txt is still claimed.
        string? manifest = FirstExisting(workingDirectory, "requirements.txt", "pyproject.toml", "uv.lock", "poetry.lock");
        if (manifest is not null)
        {
            return $"found {manifest}";
        }

        // Fallback: any *.py at the project root.
        if (Directory.EnumerateFiles(workingDirectory.FullName, "*.py", SearchOption.TopDirectoryOnly).Any())
        {
            return "found *.py file";
        }

        return null;
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
}
