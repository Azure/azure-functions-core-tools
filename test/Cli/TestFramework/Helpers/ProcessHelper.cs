// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Azure.Functions.Cli.TestFramework.Helpers
{
    public class ProcessHelper
    {
        private static readonly string _functionsHostUrl = "http://localhost";
        private static readonly TimeSpan _functionInvocationPollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan _functionInvocationPollTimeout = TimeSpan.FromSeconds(10);

        public static async Task WaitForFunctionHostToStart(
            Process funcProcess,
            int port,
            StreamWriter fileWriter)
        {
            string url = $"{_functionsHostUrl}:{port}";
            using var httpClient = new HttpClient();

            void LogMessage(string message)
            {
                Console.WriteLine(message);
                fileWriter?.WriteLine($"[HOST STATUS] {message}");
                fileWriter?.Flush();
            }

            LogMessage($"Starting to wait for function host on {url} at {DateTime.Now}");
            LogMessage($"PID of process: {funcProcess.Id}");
            int retry = 1;

            await RetryHelper.RetryAsync(async () =>
            {
                try
                {
                    LogMessage($"Retry number: {retry}");
                    fileWriter?.Flush();
                    retry += 1;

                    if (funcProcess.HasExited)
                    {
                        LogMessage($"Function host process exited with code {funcProcess.ExitCode} - cannot continue waiting");
                        throw new HostExitedBeforeReadyException(funcProcess.ExitCode);
                    }

                    // Try ping endpoint
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    HttpResponseMessage pingResponse = await httpClient.GetAsync($"{url}/admin/host/ping", cts.Token);

                    if (pingResponse.IsSuccessStatusCode)
                    {
                        LogMessage("Host responded to ping - assuming it's running");
                        return true;
                    }

                    return false;
                }
                catch (HostExitedBeforeReadyException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogMessage($"Error checking host status: {ex.Message}");
                    return false;
                }
            });
        }

        public static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
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

        public static async Task<string> ProcessStartedHandlerHelper(int port, Process process, StreamWriter fileWriter, string functionCall = "", bool shouldDelayForLogs = false)
        {
            string capturedContent = string.Empty;
            try
            {
                fileWriter.WriteLine("[HANDLER] Starting process started handler helper");
                fileWriter.WriteLine($"[HANDLER] Process working directory: {process.StartInfo.WorkingDirectory}");
                fileWriter.Flush();

                await WaitForFunctionHostToStart(process, port, fileWriter);

                fileWriter.WriteLine("[HANDLER] Host has started");
                fileWriter.Flush();

                if (!string.IsNullOrEmpty(functionCall))
                {
                    capturedContent = await InvokeFunctionWithRetryAsync(port, functionCall, fileWriter);
                    fileWriter.WriteLine($"[HANDLER] Captured content: {capturedContent}");
                    fileWriter.Flush();
                }
            }
            finally
            {
                fileWriter.WriteLine("[HANDLER] Going to kill process");
                fileWriter.Flush();

                // Wait 5 seconds for all the logs to show up first if we need them
                if (shouldDelayForLogs)
                {
                    await Task.Delay(5000);
                }

                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }

            fileWriter.WriteLine("[HANDLER] Returning captured content");
            fileWriter.Flush();
            return capturedContent;
        }

        /// <summary>
        /// Polls the function endpoint until it returns a 2xx response, the host process exits,
        /// or the timeout elapses. The host's /admin/host/ping endpoint can return success before
        /// functions are fully indexed; this absorbs the indexing race so callers see content,
        /// not an opaque empty body.
        /// </summary>
        private static async Task<string> InvokeFunctionWithRetryAsync(int port, string functionCall, StreamWriter fileWriter)
        {
            using var client = new HttpClient();
            var deadline = DateTime.UtcNow + _functionInvocationPollTimeout;
            HttpResponseMessage lastResponse = null;
            string lastBody = string.Empty;

            while (true)
            {
                lastResponse = await client.GetAsync($"http://localhost:{port}/api/{functionCall}");
                lastBody = await lastResponse.Content.ReadAsStringAsync();

                if (lastResponse.IsSuccessStatusCode)
                {
                    return lastBody;
                }

                fileWriter.WriteLine($"[HANDLER] Function returned {(int)lastResponse.StatusCode}; retrying...");
                fileWriter.Flush();

                if (DateTime.UtcNow >= deadline)
                {
                    throw new HttpRequestException(
                        $"Function call '{functionCall}' did not return success within {_functionInvocationPollTimeout.TotalSeconds:F0}s. " +
                        $"Last status: {(int)lastResponse.StatusCode} {lastResponse.ReasonPhrase}. Body: {lastBody}");
                }

                await Task.Delay(_functionInvocationPollInterval);
            }
        }
    }

    /// <summary>
    /// Thrown by <see cref="ProcessHelper.WaitForFunctionHostToStart"/> when the host process
    /// exits before it becomes ready to serve requests. Carries the host's exit code so test
    /// failures point at the real cause instead of a downstream empty-body assertion.
    /// </summary>
    public class HostExitedBeforeReadyException : Exception
    {
        public HostExitedBeforeReadyException(int exitCode)
            : base($"Function host process exited with code {exitCode} before becoming ready.")
        {
            ExitCode = exitCode;
        }

        public int ExitCode { get; }
    }
}
