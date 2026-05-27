// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Tests.Quickstart;

/// <summary>
/// Test double for <see cref="IGitRunner"/> that records calls and allows
/// configuring responses without spawning real git processes.
/// </summary>
internal sealed class FakeGitRunner : IGitRunner
{
    private readonly string? _version;
    private readonly GitRunnerException? _exception;
    private readonly Action<IReadOnlyList<string>, string?>? _onRun;
    private readonly Func<IReadOnlyList<string>, string?>? _onRunWithOutput;

    private readonly List<(IReadOnlyList<string> Arguments, string? WorkingDirectory)> _calls = [];

    public IReadOnlyList<(IReadOnlyList<string> Arguments, string? WorkingDirectory)> Calls => _calls;

    public FakeGitRunner(string? version = "git version 2.43.0", GitRunnerException? exception = null, Action<IReadOnlyList<string>, string?>? onRun = null, Func<IReadOnlyList<string>, string?>? onRunWithOutput = null)
    {
        _version = version;
        _exception = exception;
        _onRun = onRun;
        _onRunWithOutput = onRunWithOutput;
    }

    public Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        _calls.Add((arguments, workingDirectory));

        if (_exception is not null)
        {
            throw _exception;
        }

        _onRun?.Invoke(arguments, workingDirectory);
        return Task.CompletedTask;
    }

    public Task<string> RunWithOutputAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        _calls.Add((arguments, workingDirectory));

        if (_exception is not null)
        {
            throw _exception;
        }

        _onRun?.Invoke(arguments, workingDirectory);
        string output = _onRunWithOutput?.Invoke(arguments) ?? string.Empty;
        return Task.FromResult(output);
    }

    public Task<string?> TryGetVersionAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_version);
    }
}
