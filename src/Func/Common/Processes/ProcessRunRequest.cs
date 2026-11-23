// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common.Processes;

/// <summary>
/// Describes a child process invocation routed through <see cref="IProcessRunner"/>.
/// </summary>
internal sealed record ProcessRunRequest(
    string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory, TimeSpan Timeout,
    string? StandardInput = null, int MaximumStandardInputBytes = 1_048_576, int MaximumCapturedOutputCharacters = 1_048_576,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null)
{
    // Stdin can contain a private clipboard selection; diagnostic formatting must not expose it.
    public override string ToString() => $"{nameof(ProcessRunRequest)} {{ FileName = {FileName}, Timeout = {Timeout}, StandardInput = <redacted> }}";
}
