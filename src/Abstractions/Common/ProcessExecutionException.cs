// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Base exception for child process failures. Carries the exit code, captured
/// output streams, and the command string so callers can inspect any of them
/// without down-casting to a tool-specific subclass.
/// </summary>
public class ProcessExecutionException(int exitCode, string standardError, string standardOutput, string command, Exception? innerException = null)
    : Exception(BuildMessage(exitCode, standardError, command), innerException)
{
    /// <summary>
    /// The process exit code.
    /// </summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    /// Captured standard error output.
    /// </summary>
    public string StandardError { get; } = standardError;

    /// <summary>
    /// Captured standard output.
    /// </summary>
    public string StandardOutput { get; } = standardOutput;

    /// <summary>
    /// The command string that was executed.
    /// </summary>
    public string Command { get; } = command;

    private static string BuildMessage(int exitCode, string stderr, string command)
    {
        return $"'{command}' failed with exit code {exitCode}.{Environment.NewLine}{stderr}".TrimEnd();
    }
}
