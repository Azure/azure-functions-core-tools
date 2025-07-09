// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/Extensions/AnsiExtensions.cs
namespace Azure.Functions.Cli.Abstractions
{
    public static class AnsiExtensions
    {
        private static readonly Lazy<bool> s_xtermEnabled = new(
            () =>
            {
                var environment = Environment.GetEnvironmentVariable("TERM");
                if (!string.IsNullOrWhiteSpace(environment))
                {
                    return environment.IndexOf("xterm", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                return false;
            });

        public static string Black(this string text)
        {
            return "\x1B[30m" + text + "\x1B[39m";
        }

        public static string Red(this string text)
        {
            return "\x1B[31m" + text + "\x1B[39m";
        }

        public static string Green(this string text)
        {
            return "\x1B[32m" + text + "\x1B[39m";
        }

        public static string Yellow(this string text)
        {
            return "\x1B[33m" + text + "\x1B[39m";
        }

        public static string Blue(this string text)
        {
            return "\x1B[34m" + text + "\x1B[39m";
        }

        public static string Magenta(this string text)
        {
            return "\x1B[35m" + text + "\x1B[39m";
        }

        public static string Cyan(this string text)
        {
            return "\x1B[36m" + text + "\x1B[39m";
        }

        public static string White(this string text)
        {
            return "\x1B[37m" + text + "\x1B[39m";
        }

        public static string Bold(this string text)
        {
            return "\x1B[1m" + text + "\x1B[22m";
        }

        /// <summary>
        /// Wraps a string with ANSI escape codes to display it as a clickable URL in supported terminals.
        /// </summary>
        /// <param name="url">The URL to be wrapped.</param>
        /// <param name="displayText">The URL display text.</param>
        /// <returns>A string containing the URL wrapped with ANSI escape codes.</returns>
        public static string Url(this string url, string displayText)
        {
            return s_xtermEnabled.Value
                ? "\x1B]8;;" + url + "\x1b\\" + displayText + "\x1b]8;;\x1b\\"
                : url;
        }
    }
}
