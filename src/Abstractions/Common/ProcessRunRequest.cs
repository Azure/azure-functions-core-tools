// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Describes a child process invocation routed through <see cref="IProcessRunner"/>.
/// </summary>
/// <summary>
/// Describes a child process invocation routed through <see cref="IProcessRunner"/>.
/// </summary>
/// <param name="FileName">The executable to run.</param>
/// <param name="Arguments">Command-line arguments.</param>
/// <param name="WorkingDirectory">Optional working directory for the process.</param>
/// <param name="Timeout">Maximum time to wait for the process to exit.</param>
/// <param name="EnvironmentVariables">Optional per-process environment variable overrides.</param>
public sealed record ProcessRunRequest(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory, TimeSpan Timeout, IReadOnlyDictionary<string, string>? EnvironmentVariables = null);
