// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// Describes a child process invocation routed through <see cref="IProcessRunner"/>.
/// </summary>
internal sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    TimeSpan Timeout);
