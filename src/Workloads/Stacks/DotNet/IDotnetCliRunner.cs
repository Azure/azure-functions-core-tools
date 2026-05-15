// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workload.DotNet;

/// <summary>
/// Runs <c>dotnet</c> CLI commands in a child process.
/// </summary>
internal interface IDotnetCliRunner
{
    /// <summary>
    /// Executes <c>dotnet</c> with the supplied arguments.
    /// </summary>
    /// <param name="arguments">The arguments to pass to <c>dotnet</c>.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The process could not start or exited with a non-zero code.</exception>
    public Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);
}
