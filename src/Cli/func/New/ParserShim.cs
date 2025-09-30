// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Autofac;

namespace Azure.Functions.Cli;

internal static class ParserShim
{
    public static async Task<int> InvokeAsync(string[] args, IContainer container, CancellationToken ct = default)
    {
        // Preserve legacy positional `help` behavior for now
        if (args.Length < 1 ||
            (args.Length > 0 && string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase)))
        {
            // This will exit the process with the legacy code’s exit code.
            await ConsoleApp.RunAsync<ConsoleApp>(args, container, ct);
            return 0;
        }

        var parseResult = Parser.Parse(args);

        // Handle new root behaviors
        if (parseResult.GetValue(Parser.VersionOption) && parseResult.Tokens.Count == 1)
        {
            return Parser.Invoke(parseResult);
        }

        // If parse succeeded or errors aren’t “unknown”, stay in new world
        if (!HasOnlyUnrecognizedErrors(parseResult))
        {
            return await Parser.InvokeAsync(parseResult, ct);
        }

        // Unknown command/option → fall back to legacy; legacy will decide which IAction to run and will Environment.Exit(...)
        await ConsoleApp.RunAsync<ConsoleApp>(args, container, ct);
        return 0;
    }

    private static bool HasOnlyUnrecognizedErrors(ParseResult pr)
    {
        if (pr.Errors.Count == 0)
        {
            return false;
        }

        foreach (var e in pr.Errors)
        {
            var m = e.Message?.ToLowerInvariant() ?? string.Empty;
            if (!(m.Contains("unrecognized") || m.Contains("unknown option") || m.Contains("unknown command")))
            {
                return false;
            }
        }

        return true;
    }
}
