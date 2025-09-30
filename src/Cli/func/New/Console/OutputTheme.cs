// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;

namespace Azure.Functions.Cli.New
{
    public static class OutputTheme
    {
        // === String-returning helpers (use with AnsiConsole.Markup/MarkupLine) ===
        // AnsiConsole.MarkupLine(OutputTheme.TitleColor("Azure Functions CLI"));
        // AnsiConsole.MarkupLine($"HTTP URL: {OutputTheme.HttpFunctionUrlColor("https://localhost:7071/api/hello")}");
        public static string TitleColor(string value) => Colorize(value, "cyan", dim: false);

        public static string VerboseColor(string value) => Colorize(value, "green",   dim: true);

        public static string LinksColor(string value) => Colorize(value, "cyan",    underline: true, dim: true);

        public static string AdditionalInfoColor(string value) => Colorize(value, "cyan",    dim: true);

        public static string ExampleColor(string value) => Colorize(value, "green",   dim: true);

        public static string ErrorColor(string value) => Colorize(value, "red");

        public static string QuestionColor(string value) => Colorize(value, "magenta", dim: true);

        public static string WarningColor(string value) => Colorize(value, "yellow",  dim: true);

        public static string QuietWarningColor(string value) => Colorize(value, "grey"); // subtle

        public static string HttpFunctionNameColor(string value) => Colorize(value, "yellow",  dim: true);

        public static string HttpFunctionUrlColor(string value) => Colorize(value, "green",   dim: true);

        // Core util: wrap Spectre markup safely
        private static string Colorize(string value, string color, bool underline = false, bool dim = false, bool bold = false)
        {
            var mods = (underline ? " underline" : string.Empty) +
                       (dim ? " dim" : string.Empty) +
                       (bold ? " bold" : string.Empty);
            return $"[{color}{mods}]{Markup.Escape(value)}[/]";
        }
    }
}
