// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workload.Go;

/// <summary>
/// Resolves a directory as a Go Functions project when <c>host.json</c> is present
/// alongside a <c>go.mod</c> or any <c>*.go</c> file at the project root.
/// </summary>
internal sealed class GoProjectResolver : IProjectResolver
{
    private const string WorkerRuntime = "native";

    public Task<EvaluationResult> EvaluateAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!workingDirectory.Exists || !File.Exists(Path.Combine(workingDirectory.FullName, "host.json")))
        {
            return Task.FromResult(EvaluationResult.NoMatch("no host.json"));
        }

        if (File.Exists(Path.Combine(workingDirectory.FullName, "go.mod")))
        {
            return Task.FromResult(EvaluationResult.Match("found go.mod", WorkerRuntime));
        }

        if (Directory.EnumerateFiles(workingDirectory.FullName, "*.go", SearchOption.TopDirectoryOnly).Any())
        {
            return Task.FromResult(EvaluationResult.Match("found *.go file", WorkerRuntime));
        }

        return Task.FromResult(EvaluationResult.NoMatch("host.json present but no Go fingerprint file"));
    }
}
