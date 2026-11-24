// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// The values <c>func init</c> parsed before routing to a flow.
/// </summary>
/// <param name="ParseResult">The parse result for <c>func init</c>.</param>
/// <param name="WorkingDirectory">The project directory.</param>
/// <param name="ProjectName">The requested project name, if any.</param>
/// <param name="RequestedStack">The requested stack, if any.</param>
/// <param name="Language">The requested language, normalized to lower case, if any.</param>
/// <param name="Force">Whether the directory may be re-initialized.</param>
/// <param name="StackOptions">Options contributed by installed stacks.</param>
/// <param name="ReservedOptions">Options owned by <c>func init</c> that a template may not redefine.</param>
/// <param name="UnmatchedTokens">Tokens no option or argument consumed, in command-line order.</param>
/// <param name="TemplateTokens">
/// Tokens a project template binds, in command-line order: unmatched tokens and tokens of <paramref name="StackOptions"/>.
/// </param>
internal sealed record InitRequest(
    ParseResult ParseResult,
    WorkingDirectory WorkingDirectory,
    string? ProjectName,
    string? RequestedStack,
    string? Language,
    bool Force,
    IReadOnlyList<Option> StackOptions,
    IReadOnlyList<Option> ReservedOptions,
    IReadOnlyList<string> UnmatchedTokens,
    IReadOnlyList<string> TemplateTokens);
