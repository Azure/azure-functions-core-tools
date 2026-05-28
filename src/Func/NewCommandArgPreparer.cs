// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli;

/// <summary>
/// Pre-parse hydration for <c>func new -t &lt;id&gt;</c>. The user is
/// allowed to pass per-template options on the same invocation as
/// <c>--template &lt;id&gt;</c> (e.g.
/// <c>func new -t HttpTrigger -n MyHttp --auth-level anonymous</c>). The
/// hydrated <see cref="Option"/> set isn't known until the runner has
/// resolved the project + located the chosen template, but System.CommandLine
/// would reject the unknown options before <c>NewCommand.ExecuteAsync</c>
/// ever runs (PathArgument's unrecognized-token guard catches them first).
/// </summary>
/// <remarks>
/// This pre-parse is intentionally rudimentary — it only looks for
/// <c>new</c> as the first non-flag token and <c>--template</c>/<c>-t</c>
/// in argv. Any failure (project not init'd, workload not installed,
/// template not found) falls through silently; the main parser will
/// surface the right diagnostics in the normal flow.
/// </remarks>
internal static class NewCommandArgPreparer
{
    public static void PrepareIfFuncNew(string[] args, IServiceProvider services, Command rootCommand)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootCommand);

        if (!LooksLikeFuncNew(args))
        {
            return;
        }

        string? templateId = ExtractTemplateId(args);
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return;
        }

        NewCommand? newCommand = rootCommand.Subcommands
            .OfType<NewCommand>()
            .FirstOrDefault();
        if (newCommand is null)
        {
            return;
        }

        // Delegate the actual hydration to NewCommand (it already owns the
        // attach/detach lifecycle for the help path).
        try
        {
            newCommand.AttachHydratedOptionsForPreParse(templateId);
        }
        catch
        {
            // Hydration failures here are silent on purpose — the main
            // parser will produce a much nicer error message a moment
            // later (e.g. "Template 'X' was not found for this project's
            // stack").
        }
    }

    private static bool LooksLikeFuncNew(string[] args)
    {
        // Find the first non-flag token; that's the verb. We're looking
        // for "new" — anything else means this isn't our concern.
        foreach (string arg in args)
        {
            if (string.IsNullOrEmpty(arg))
            {
                continue;
            }

            if (arg.StartsWith('-'))
            {
                continue;
            }

            return string.Equals(arg, "new", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? ExtractTemplateId(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.IsNullOrEmpty(arg))
            {
                continue;
            }

            // Long form: --template <id> or --template=<id>.
            if (arg.Equals("--template", StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }
            if (arg.StartsWith("--template=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--template=".Length..];
            }

            // Short form: -t <id> or -t=<id>.
            if (arg.Equals("-t", StringComparison.Ordinal))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }
            if (arg.StartsWith("-t=", StringComparison.Ordinal))
            {
                return arg["-t=".Length..];
            }
        }

        return null;
    }
}
