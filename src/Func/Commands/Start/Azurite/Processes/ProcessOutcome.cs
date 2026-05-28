// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// Captured outcome of a child process invocation.
/// </summary>
/// <param name="ExitCode">The process exit code, or <c>null</c> when the process could not be started.</param>
/// <param name="StandardOutput">Captured standard output, possibly empty.</param>
/// <param name="StandardError">Captured standard error, possibly empty.</param>
/// <param name="TimedOut"><c>true</c> when the process was killed because the per-request timeout elapsed.</param>
/// <param name="ExecutableNotFound"><c>true</c> when the executable could not be located on the host.</param>
internal sealed record ProcessOutcome(int? ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool ExecutableNotFound);
