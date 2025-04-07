﻿using System.Diagnostics;
using System.Net.Sockets;
using System.Net;

namespace TestFramework.Helpers
{
    public class ProcessHelper
    {
        private static string FunctionsHostUrl = "http://localhost";

        public static async Task WaitForFunctionHostToStart(
             Process funcProcess,
             int port,
             StreamWriter? fileWriter = null,
             string? functionCall = null,
             int timeout = 120 * 1000,
             HttpStatusCode expectedStatus = HttpStatusCode.OK)
        {
            var url = $"{FunctionsHostUrl}:{port.ToString()}";
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5) // 5-second timeout for each request
            };

            void LogMessage(string message)
            {
                Console.WriteLine(message);
                fileWriter?.WriteLine($"[HOST STATUS] {message}");
                fileWriter?.Flush();
            }

            LogMessage($"Starting to wait for function host on {url} at {DateTime.Now}");
            LogMessage($"Current directory: {Directory.GetCurrentDirectory()}");

            LogMessage($"PID of process: {funcProcess.Id}");
            fileWriter?.Flush();
            int retry = 1;

            await RetryHelper.RetryAsync((async () =>
            {
                try
                {
                    LogMessage($"Retry number: {retry}");
                    fileWriter?.Flush();
                    retry += 1;

                    if (funcProcess.HasExited)
                    {
                        LogMessage($"Function host process exited with code {funcProcess.ExitCode} - cannot continue waiting");
                        throw new InvalidOperationException($"Process exited with code {funcProcess.ExitCode}");
                    }

                    LogMessage($"Trying to get ping response");

                    // Try ping endpoint as a fallback
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var pingResponse = await httpClient.GetAsync($"{url}/admin/host/ping", cts.Token);

                    LogMessage($"Got ping response");

                    fileWriter?.Flush();
                    if (pingResponse.IsSuccessStatusCode)
                    {
                        LogMessage("Host responded to ping - assuming it's running");
                        return true;
                    }

                    LogMessage($"Returning false");

                    return false;
                }
                catch (Exception ex)
                {
                    LogMessage($"Error checking host status: {ex.Message}");
                    return false;
                }
            }));
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

        public static async Task<string> ProcessStartedHandlerHelper(int port, Process process,
    StreamWriter? fileWriter, string functionCall = "")
        {
            string capturedContent = "";
            try
            {
                fileWriter.WriteLine("[HANDLER] Starting process started handler helper");
                fileWriter.Flush();

                fileWriter.WriteLine($"[HANDLER] Process working directory: {process.StartInfo.WorkingDirectory}");

                //await WaitForFunctionHostToStart(process, port, fileWriter);

                fileWriter.WriteLine("[HANDLER] Host has started");
                fileWriter.Flush();

                if (!string.IsNullOrEmpty(functionCall))
                {
                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync($"http://localhost:{port}/api/{functionCall}");
                        capturedContent = await response.Content.ReadAsStringAsync();
                        fileWriter.WriteLine($"[HANDLER] Captured content: {capturedContent}");
                        fileWriter.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                fileWriter.WriteLine($"[HANDLER] Caught the following exception: {ex.Message}");
                fileWriter.Flush();
            }
            finally
            {
                fileWriter.WriteLine($"[HANDLER] Going to kill process");
                fileWriter.Flush();
                process.Kill(true);
            }
            fileWriter.WriteLine($"[HANDLER] Returning captured content");
            fileWriter.Flush();
            return capturedContent;
        }

    }
}
