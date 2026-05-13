// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Resolves a low-cardinality command name for telemetry from a parsed
/// command line. The matched command path (e.g. <c>"workload list"</c>)
/// is preferred so dashboards can group by command without exploding into
/// per-invocation variants from raw <c>args</c>.
/// </summary>
internal static class CommandNameResolver
{
    /// <summary>
    /// Resolves a telemetry-friendly command name from a parsed result.
    /// Walks the matched command up to (but excluding) <paramref name="rootCommand"/>
    /// and joins each segment with a space. When only the root matched and
    /// no subcommand was given, returns:
    /// <list type="bullet">
    /// <item><description><c>"unknown"</c> when the parse produced errors (e.g. a typo),</description></item>
    /// <item><description><c>"version"</c> when <see cref="FuncRootCommand.VerboseOption"/>
    /// is set (the root action prints detailed version),</description></item>
    /// <item><description><c>"help"</c> otherwise (the root action shows help).</description></item>
    /// </list>
    /// </summary>
    public static string ResolveCommandName(ParseResult parseResult, FuncRootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(rootCommand);

        var parts = new List<string>();
        Command? current = parseResult.CommandResult.Command;
        while (current is not null && current != rootCommand)
        {
            parts.Insert(0, current.Name);
            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        if (parts.Count > 0)
        {
            return string.Join(' ', parts);
        }

        if (parseResult.Errors.Count > 0)
        {
            return "unknown";
        }

        return parseResult.GetValue(rootCommand.VerboseOption) ? "version" : "help";
    }
}
