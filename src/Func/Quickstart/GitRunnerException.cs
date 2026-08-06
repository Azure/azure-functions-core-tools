// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Thrown when a <c>git</c> child process exits with a non-zero exit code.
/// </summary>
internal sealed class GitRunnerException(int exitCode, string standardError, string standardOutput, string command, Exception? innerException = null)
    : ProcessExecutionException(exitCode, standardError, standardOutput, $"git {command}", innerException)
{
}
