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
    /// <summary>
    /// Gets the unparsed command-line arguments, passed through as-is. Use instead of <see cref="Arguments"/>, which must then be empty.
    /// </summary>
    public string? ArgumentString { get; init; }

    /// <summary>
    /// Gets a value indicating whether standard output is captured; otherwise the child writes to the CLI's own output.
    /// </summary>
    public bool RedirectStandardOutput { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether standard error is captured; otherwise the child writes to the CLI's own error output.
    /// </summary>
    public bool RedirectStandardError { get; init; } = true;

    // Stdin can contain a private clipboard selection; diagnostic formatting must not expose it.
    public override string ToString() => $"{nameof(ProcessRunRequest)} {{ FileName = {FileName}, Timeout = {Timeout}, StandardInput = <redacted> }}";
}
