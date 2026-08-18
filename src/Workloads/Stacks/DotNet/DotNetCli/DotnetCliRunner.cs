// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Executes <c>dotnet</c> CLI commands in a child process.
/// </summary>
internal sealed class DotnetCliRunner(IDotnetPathResolver pathResolver, IProcessRunner processRunner) : IDotnetCliRunner
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(10);

    private readonly IDotnetPathResolver _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessRunRequest request = new(_pathResolver.Resolve(), arguments, workingDirectory, _processTimeout);
        ProcessOutcome outcome = await _processRunner.RunAsync(request, cancellationToken);

        if (outcome.ExecutableNotFound)
        {
            throw new InvalidOperationException("Failed to start dotnet process.");
        }

        if (outcome.ExitCode != 0)
        {
            throw new DotnetCliException(outcome.ExitCode!.Value, outcome.StandardError, outcome.StandardOutput, string.Join(' ', arguments));
        }
    }

    public async Task RunStreamingAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<string>? onOutputLine,
        Action<string>? onErrorLine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessRunRequest request = new(_pathResolver.Resolve(), arguments, workingDirectory, _processTimeout);
        ProcessOutcome outcome = await _processRunner.RunAsync(request, cancellationToken);

        if (outcome.ExecutableNotFound)
        {
            throw new InvalidOperationException("Failed to start dotnet process.");
        }

        // Deliver the captured output through the streaming callbacks so callers
        // that registered callbacks still see the output lines.
        DeliverCapturedOutput(outcome.StandardOutput, onOutputLine);
        DeliverCapturedOutput(outcome.StandardError, onErrorLine);

        if (outcome.ExitCode != 0)
        {
            throw new DotnetCliException(outcome.ExitCode!.Value, outcome.StandardError, outcome.StandardOutput, string.Join(' ', arguments));
        }
    }

    private static void DeliverCapturedOutput(string captured, Action<string>? callback)
    {
        if (callback is null || string.IsNullOrEmpty(captured))
        {
            return;
        }

        foreach (string line in captured.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length > 0)
            {
                callback(trimmed);
            }
        }
    }
}
