using Azure.Functions.Cli.Common;
using Colors.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    public static class NpmHelper
    {

        public async static Task Install()
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

        private static async Task<(string output, string error, int exitCode)> InternalRunCommand(string command, string args, bool ignoreError, string stdIn = null)
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

            return (trim(sbOutput.ToString()), trim(sbError.ToString()), exitCode);

            string trim(string str) => str.Trim(new[] { ' ', '\n' });
        }
    }
}
