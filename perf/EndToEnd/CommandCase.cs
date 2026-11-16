// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Azure.Functions.Cli.Performance.EndToEnd;

internal sealed record CommandCase(string Name, string[] Arguments, string OutputPattern, int ExitCode = 0)
{
    internal static IReadOnlyList<CommandCase> All { get; } =
    [
        new("version", ["version"], @"\A\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?\s*\z"),
        new("help", ["--help"], @"\A[\s\S]*\z"),
    ];

    internal string DisplayCommand => string.Join(" ", Arguments.Select(FormatArgument).Prepend("func"));

    internal void Validate(int exitCode, string output, string error)
    {
        if (exitCode != ExitCode)
        {
            throw new InvalidOperationException($"func {Name} exited with {exitCode}, expected {ExitCode}. stderr: {error}");
        }

        if (!Regex.IsMatch(output, OutputPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new InvalidOperationException($"func {Name} produced unexpected stdout: {output}. stderr: {error}");
        }
    }

    private static string FormatArgument(string argument)
    {
        if (argument.Length > 0 && argument.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':' or '=' or '+' or '@'))
        {
            return argument;
        }

        return JsonSerializer.Serialize(argument).Replace("|", @"\u007C", StringComparison.Ordinal);
    }
}
