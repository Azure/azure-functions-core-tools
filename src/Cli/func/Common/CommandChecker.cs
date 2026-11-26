// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    public static class CommandChecker
    {
        public static bool CommandValid(string fileName, string args)
            => CheckExitCode(fileName, args);

        // c:\windows\system32\where.exe
        public static bool CommandExists(string command)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var wherePath = GetWindowsWherePath();
                if (File.Exists(wherePath))
                {
                    return CheckExitCode(wherePath, command);
                }
                else
                {
                    ColoredConsole.WriteLine(WarningColor($"The 'where' command executable was not found at the expected path at {Environment.SystemDirectory}."));
                    return CheckExitCode("where", command);
                }
            }
            else
            {
                // Not every system has /bin/bash (for example NixOS) or /bin/sh. Try /bin/sh first, then /bin/bash
                // (the previous behavior), and only then resolve "sh" through PATH.
                return UnixCommandExists(command, ["/bin/sh", "/bin/bash"], "sh");
            }
        }

        internal static bool UnixCommandExists(string command, IEnumerable<string> shellPaths, string pathShell)
        {
            var shell = shellPaths.FirstOrDefault(File.Exists) ?? pathShell;

            try
            {
                return CheckExitCode(shell, $"-c \"command -v {command}\"");
            }
            catch (Win32Exception)
            {
                // No usable shell on this system, so the command cannot be confirmed.
                ColoredConsole.WriteLine(WarningColor($"Could not find a shell (/bin/sh, /bin/bash or 'sh' on PATH) to check whether '{command}' exists."));
                return false;
            }
        }

        public static async Task<bool> PowerShellModuleExistsAsync(string powershellExecutable, string module)
        {
            // Attempt to get the specified module. If it cannot be found, throw.
            var exe = new Executable(
                powershellExecutable,
                $"-NonInteractive -o Text -NoProfile -c if(!(Get-Module -ListAvailable {module})) {{ throw '{module} module not found' }}");

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var exitCode = await exe.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
            return exitCode == 0;
        }

        private static bool CheckExitCode(string fileName, string args, int expectedExitCode = 0)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var process = Process.Start(processStartInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }

        public static string GetWindowsWherePath()
        {
            return $"{Environment.SystemDirectory}{Path.DirectorySeparatorChar}where.exe";
        }
    }
}
