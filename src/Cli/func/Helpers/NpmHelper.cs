// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.Helpers
{
    public static class NpmHelper
    {
        public static async Task Install()
        {
            await RunNpmCommand("install", true);
        }

        internal static async Task RunNpmCommand(string args, bool ignoreError = true, bool showProgress = true, string stdIn = null)
        {
            if (showProgress || StaticSettings.IsDebug)
            {
                ColoredConsole.Write($"Running 'npm {args}'...");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await InternalRunCommand("cmd", $"/c npm {args}", ignoreError, stdIn: stdIn);
            }
            else
            {
                await InternalRunCommand("npm", args, ignoreError, stdIn: stdIn);
            }
        }

        private static async Task<(string Output, string Error, int ExitCode)> InternalRunCommand(string command, string args, bool ignoreError, string stdIn = null)
        {
            var npm = new Executable(command, args);
            var sbError = new StringBuilder();
            var sbOutput = new StringBuilder();

            var exitCode = await npm.RunAsync(l => sbOutput.AppendLine(l), e => sbError.AppendLine(e), stdIn: stdIn);

            if (exitCode != 0 && !ignoreError)
            {
                throw new CliException($"Error running {npm.Command}.\n" +
                    $"output: {sbOutput.ToString()}\n{sbError.ToString()}");
            }

            return (Trim(sbOutput.ToString()), Trim(sbError.ToString()), exitCode);

            string Trim(string str) => str.Trim([' ', '\n']);
        }
    }
}
