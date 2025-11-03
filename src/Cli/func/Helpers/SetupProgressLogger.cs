// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Helpers
{
    internal static class SetupProgressLogger
    {
        static SetupProgressLogger()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                 /* ignore */
            }
        }

        public static void Section(string text) =>
            ColoredConsole.WriteLine($"\n{DarkGray($"▸ {text}")}");

        public static void Step(string scope, string text) =>
            ColoredConsole.WriteLine($"{Gray($"[{scope}]")} {text}");

        public static void Ok(string scope, string text) =>
            ColoredConsole.WriteLine($"{Gray($"[{scope}]")} {Green($"✓ {text}")}");

        public static void Info(string scope, string text) =>
            ColoredConsole.WriteLine($"{Gray($"[{scope}]")} {text}");

        public static void Warn(string scope, string text) =>
            ColoredConsole.WriteLine($"{Gray($"[{scope}]")} {Yellow($"⚠ {text}")}");

        public static void FileFound(string scope, string path) =>
            Step(scope, $"Found at {path}");

        public static void FileCreated(string scope, string path) =>
            Step(scope, $"Creating new at {path}");
    }
}
