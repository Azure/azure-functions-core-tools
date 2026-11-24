// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// The values <c>func init</c> parsed before routing to a flow.
/// </summary>
/// <param name="WorkingDirectory">The project directory.</param>
/// <param name="ProjectName">The requested project name, if any.</param>
/// <param name="RequestedStack">The requested stack, if any.</param>
/// <param name="Language">The requested language, normalized to lower case, if any.</param>
/// <param name="Force">Whether the directory may be re-initialized.</param>
/// <param name="StackOptionsCommand">
/// The command holding the options contributed by installed stacks. Only routes that consume stack options parse
/// <paramref name="UnmatchedTokens"/> against it.
/// </param>
/// <param name="ReservedOptions">Options owned by <c>func init</c> that a template may not redefine.</param>
/// <param name="UnmatchedTokens">
/// Tokens after the built-in options and the project path, in command-line order. The selected route interprets them:
/// the template flow binds them against the selected template, and the legacy flow against the stack options.
/// </param>
internal sealed record InitRequest(
    WorkingDirectory WorkingDirectory,
    string? ProjectName,
    string? RequestedStack,
    string? Language,
    bool Force,
    Command StackOptionsCommand,
    IReadOnlyList<Option> ReservedOptions,
    IReadOnlyList<string> UnmatchedTokens);
