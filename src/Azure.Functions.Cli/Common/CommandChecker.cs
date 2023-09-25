using Azure.Functions.Cli.Helpers;
using Colors.Net;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    public static class CommandChecker
    {
        public static bool CommandValid(string fileName, string args)
            => CheckExitCode(fileName, args);

        public static bool CommandExists(string command, out string commandPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var wherePath = $"{Environment.SystemDirectory}{Path.DirectorySeparatorChar}where.exe";
                if (!File.Exists(wherePath))
                {
                    throw new CliException($"The 'where' command executable was not found at the expected path at {Environment.SystemDirectory}.");
                }

                if (PythonHelpers.InVirtualEnvironment)
                {
                    commandPath = command;
                    return CheckExitCode(wherePath, command);
                }
                else
                {
                    (bool isValid, string path) = CheckWindowsValidCommand(wherePath, command);
                    commandPath = path;
                    return isValid;
                }
            }
            else
            {
                commandPath = command;
                return CheckExitCode("/bin/bash", $"-c \"command -v {command}\"");
            }
        }

        public static async Task<bool> PowerShellModuleExistsAsync(string powershellExecutable, string module)
        {
            // Attempt to get the specified module. If it cannot be found, throw.
            var exe = new Executable(powershellExecutable,
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

        private static (bool, string) CheckWindowsValidCommand(string fileName, string args)
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
            if (process?.ExitCode != 0)
            {
                return (false, string.Empty);
            }

            while (process.StandardOutput.Peek() >= 0)
            {
                var responseLine = process.StandardOutput.ReadLine();
                if (!string.IsNullOrWhiteSpace(responseLine) && !responseLine.StartsWith(Environment.CurrentDirectory))
                {
                    return (true, responseLine);
                }
            }

            return (false, string.Empty);
        }
    }
}
