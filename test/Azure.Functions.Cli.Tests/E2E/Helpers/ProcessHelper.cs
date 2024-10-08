using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
        private static string FunctionsHostUrl = "http://localhost";

        public static async Task WaitForFunctionHostToStart(Process funcProcess, int port)
        {
            var url = $"{FunctionsHostUrl}:{port.ToString()}";
            using var httpClient = new HttpClient();

            await RetryHelper.RetryAsync(async () =>
            {
                try
                {
                    var response = await httpClient.GetAsync($"{url}/admin/host/status");
                    var content = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(content);

                    if (doc.RootElement.TryGetProperty("state", out JsonElement value) && value.GetString() == "Running")
                    {
                        return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static void TryKillProcessForPort(int port)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Can only use these commands on a windows device
                return;
            }

            string searchCommand = string.Format("/c netstat -anop tcp|findstr \":{0}.*LISTENING\"", port);
            string searchResult = ExecuteCommand(searchCommand).Trim();

            if (string.IsNullOrEmpty(searchResult))
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

        public static int GetAvailablePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                return port;
            }
            finally
            {
                listener.Stop();
                listener.Server.Dispose();
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
