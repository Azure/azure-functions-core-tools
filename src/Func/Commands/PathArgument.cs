// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// The shared <c>[path]</c> argument used by every command that operates on
/// a project directory (<c>func init</c>, <c>func new</c>, <c>func start</c>).
/// Binds directly to <see cref="WorkingDirectory"/> so the parser hands callers
/// the same domain type that flows through to workloads.
///
/// Existence checks and create-if-missing are caller's responsibility via
/// <see cref="WorkingDirectory.Exists"/> / <see cref="WorkingDirectory.CreateIfNotExists"/>.
///
/// A token that starts with <c>-</c> is never consumed as the path. It is left
/// unmatched so the owning command decides how to handle it: commands that treat
/// unmatched tokens as errors (the default) report it as unrecognized, and
/// template commands bind it against the selected template.
/// </summary>
internal sealed class PathArgument : Argument<WorkingDirectory>
{
    public PathArgument() : base("path")
    {
        Description = "The project directory to use (defaults to current directory)";
        // Accept the whole run of argument tokens so that tokens left behind after an option-like
        // token are returned in their original order; only the first token is ever used as the path.
        Arity = ArgumentArity.ZeroOrMore;
        CustomParser = ParseToken;
        DefaultValueFactory = _ => WorkingDirectory.FromCwd();
        Validators.Add(LeaveOptionLikeTokenUnmatched);
    }

    private static WorkingDirectory ParseToken(ArgumentResult result)
    {
        string? token = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
        return string.IsNullOrEmpty(token) || token.StartsWith('-')
            ? WorkingDirectory.FromCwd()
            : WorkingDirectory.FromExplicit(token);
    }

    private static void LeaveOptionLikeTokenUnmatched(ArgumentResult result)
    {
        if (result.Tokens.Count > 0)
        {
            result.OnlyTake(result.Tokens[0].Value.StartsWith('-') ? 0 : 1);
        }
    }
}
