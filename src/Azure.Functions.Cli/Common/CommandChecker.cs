using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Common
{
    public static class CommandChecker
    {
        public static bool CommandValid(string fileName, string args)
            => CheckExitCode(fileName, args);

        public static bool CommandExists(string command)
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? CheckExitCode("where", command)
            : CheckExitCode("bash", $"-c \"command -v {command}\"");

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
            process.WaitForExit();
            return process.ExitCode == 0;
        }
    }
}
