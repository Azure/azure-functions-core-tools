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
/// </summary>
internal sealed class PathArgument : Argument<WorkingDirectory>
{
    public PathArgument() : base("path")
    {
        Description = "The project directory to use (defaults to current directory)";
        Arity = ArgumentArity.ZeroOrOne;
        CustomParser = ParseToken;
        DefaultValueFactory = _ => WorkingDirectory.FromCwd();
    }

    private static WorkingDirectory ParseToken(ArgumentResult result)
    {
        var token = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
        if (string.IsNullOrEmpty(token))
        {
            return WorkingDirectory.FromCwd();
        }

        // Tokens that start with '-' are almost certainly mistyped options
        // (`func start --bad-flag`). Surfacing this as an unrecognized option
        // is friendlier than silently treating "--bad-flag" as a path. The
        // returned sentinel is unused — System.CommandLine short-circuits
        // invocation when the parse result has errors.
        if (token.StartsWith('-'))
        {
            result.AddError($"Unrecognized option '{token}'.");
            return WorkingDirectory.FromCwd();
        }

        return WorkingDirectory.FromExplicit(token);
    }
}
