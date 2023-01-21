using Azure.Functions.Cli.Common;
using Colors.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    public static class NpmHelper
    {

        public static void Install()
        {
            RunNpmCommand("install");
        }

        internal static void RunNpmCommand(string args, bool ignoreError = true, bool showProgress = true, string stdIn = null)
        {
            if (showProgress || StaticSettings.IsDebug)
            {
                ColoredConsole.Write($"Running 'npm {args}'.");
            }
            
            InternalRunNpmCommand(args, ignoreError, stdIn: stdIn);
        }

        private static void InternalRunNpmCommand(string args, bool ignoreError, string stdIn = null)
        {
            // Todo: It will not work on Linux or Mac. We need to use npm.cmd on Windows and npm on Linux/Mac
            var currentPath = Environment.CurrentDirectory;
            var psiNpmRunDist = new ProcessStartInfo
            {
                FileName = "cmd",
                RedirectStandardInput = true,
                WorkingDirectory = currentPath
            };
            var pNpmRunDist = Process.Start(psiNpmRunDist);
            pNpmRunDist.StandardInput.WriteLine("npm install");
            pNpmRunDist.WaitForExit();
        }
    }
}
