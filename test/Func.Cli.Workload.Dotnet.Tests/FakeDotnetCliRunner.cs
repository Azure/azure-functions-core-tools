// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workload.Dotnet;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

/// <summary>
/// A fake dotnet CLI runner that records commands and returns configured results.
/// </summary>
public class FakeDotnetCliRunner : IDotnetCliRunner
{
    private readonly List<(string Arguments, string? WorkingDirectory)> _invocations = [];
    private readonly Queue<DotnetCliResult> _results = new();

    public IReadOnlyList<(string Arguments, string? WorkingDirectory)> Invocations => _invocations;

    /// <summary>
    /// Enqueues a result to be returned by the next RunAsync call.
    /// </summary>
    public void EnqueueResult(DotnetCliResult result) => _results.Enqueue(result);

    /// <summary>
    /// Enqueues a successful result.
    /// </summary>
    public void EnqueueSuccess(string stdout = "") =>
        _results.Enqueue(new DotnetCliResult(0, stdout, ""));

    /// <summary>
    /// Enqueues a failure result.
    /// </summary>
    public void EnqueueFailure(string stderr = "error", int exitCode = 1) =>
        _results.Enqueue(new DotnetCliResult(exitCode, "", stderr));

    public Task<DotnetCliResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        _invocations.Add((arguments, workingDirectory));

        var result = _results.Count > 0
            ? _results.Dequeue()
            : new DotnetCliResult(0, "", ""); // Default to success

        return Task.FromResult(result);
    }
}
