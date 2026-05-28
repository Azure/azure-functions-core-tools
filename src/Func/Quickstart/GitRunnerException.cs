// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Thrown when a <c>git</c> child process exits with a non-zero exit code.
/// </summary>
internal sealed class GitRunnerException(int exitCode, string standardError, string standardOutput, string command, Exception? innerException = null)
    : Exception(BuildMessage(exitCode, standardError, command), innerException)
{
    public int ExitCode { get; } = exitCode;

    public string StandardError { get; } = standardError;

    public string StandardOutput { get; } = standardOutput;

    public string Command { get; } = command;

    private static string BuildMessage(int exitCode, string stderr, string command)
    {
        return $"'git {command}' failed with exit code {exitCode}.{Environment.NewLine}{stderr}".TrimEnd();
    }
}
