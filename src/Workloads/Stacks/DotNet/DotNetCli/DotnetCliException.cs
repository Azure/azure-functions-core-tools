// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Thrown when a <c>dotnet</c> CLI child process exits with a non-zero exit code.
/// </summary>
internal sealed class DotnetCliException(int exitCode, string standardError, string standardOutput, string command)
    : ProcessExecutionException(exitCode, standardError, standardOutput, $"dotnet {command}")
{
}
