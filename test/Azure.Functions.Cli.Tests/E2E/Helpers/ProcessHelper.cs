using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    internal class ProcessHelper
    {
        private const string CommandExe = "cmd";
        private static readonly Regex pidRegex = new Regex(@"LISTENING\s+(\d+)\s*$");
        private static string FunctionsHostUrl = "http://localhost:";

        public static async Task WaitForFunctionHostToStart(Process funcProcess)
        {
            // Example usage:
            // if (runConfiguration.WaitForHostToStart)
            // {
            //     await WaitForFunctionHostToStart(exe.Process);
            // }

            using var httpClient = new HttpClient();
            Console.WriteLine("Waiting for host to be running...");
            await RetryHelper.RetryAsync(async () =>
            {
                try
                {
                    var response = await httpClient.GetAsync($"{FunctionsHostUrl}/admin/host/status");
                    var content = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(content);

                    if (doc.RootElement.TryGetProperty("state", out JsonElement value) && value.GetString() == "Running")
                    {
                        Console.WriteLine($"  Current state: Running");
                        return true;
                    }

                    Console.WriteLine($"  Current state: {value}");
                    return false;
                }
                catch
                {
                    if (funcProcess.HasExited)
                    {
                        // Something went wrong starting the host - check the logs
                        Console.WriteLine($"  Current state: process exited - something may have gone wrong.");
                        return false;
                    }

                    // Can get exceptions before host is running.
                    Console.WriteLine($"  Current state: process starting");
                    return false;
                }
            });
        }

        public static void KillExistingFuncHosts()
        {
            foreach (var func in Process.GetProcessesByName("func"))
            {
                try
                {
                    func.Kill();
                }
                catch
                {
                    // Best effort
                }
            }
        }

        public static void TryKillProcessForPort(string port)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Can only use these commands on a windows device
                return;
            }

            string searchCommand = string.Format("/c netstat -anop tcp|findstr \":{0}.*LISTENING\"", port);
            string searchResult = ExecuteCommand(searchCommand);

            if (searchResult == null)
            {
                // No process running on given port
                return;
            }

            Match match = pidRegex.Match(searchResult);

            if (!match.Success)
            {
                Console.WriteLine($"Unable to parse the PID for the process running on port '{port}'");
                return;
            }

            string pid = match.Groups[1].Value;
            string killCommand = $"/c taskkill /PID {pid} /F";
            string killResult = ExecuteCommand(killCommand);

            if (killResult == null)
            {
                Console.WriteLine($"Unable to kill the process (PID: {pid}) running on port '{port}'");
            }
        }

        private static string ExecuteCommand(string command)
        {
            using (Process p = new Process())
            {
                string commandOut = string.Empty;

                p.StartInfo.FileName = CommandExe;
                p.StartInfo.Arguments = command;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();

                commandOut = p.StandardOutput.ReadToEnd();
                string errors = p.StandardError.ReadToEnd();

                try
                {
                    p.WaitForExit(TimeSpan.FromSeconds(2).Milliseconds);
                }
                catch (Exception exp)
                {
                    Console.WriteLine(exp.ToString());
                }

                return commandOut;
            }
        }
    }
}
