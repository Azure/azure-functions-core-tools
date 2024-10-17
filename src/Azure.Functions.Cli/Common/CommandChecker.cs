using Colors.Net;
using System;
using System.Diagnostics;
using System.IO;
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
        
        public static string GetWindowsWherePath()
        {
            return $"{Environment.SystemDirectory}{Path.DirectorySeparatorChar}where.exe";
        }
    }
}
