// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Thrown when a <c>dotnet</c> CLI child process exits with a non-zero exit code.
/// </summary>
internal sealed class DotnetCliException : Exception
{
    public int ExitCode { get; }

    public string StandardError { get; }

    public string StandardOutput { get; }

    public string Command { get; }

    public DotnetCliException(int exitCode, string standardError, string standardOutput, string command)
        : base(BuildMessage(exitCode, standardError, standardOutput, command))
    {
        ExitCode = exitCode;
        StandardError = standardError;
        StandardOutput = standardOutput;
        Command = command;
    }

    private static string BuildMessage(int exitCode, string stderr, string stdout, string command)
    {
        return $"'dotnet {command}' failed with exit code {exitCode}.{Environment.NewLine}{stderr}{stdout}";
    }
}
