using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Xunit.Abstractions;
using FluentAssertions;
using Azure.Functions.Cli.Abstractions;
using Func.TestFramework.Commands;

namespace Func.TestFramework.Helpers
{
    public class ProcessHelper
    {
        private static string FunctionsHostUrl = "http://localhost";

        private static SemaphoreSlim _functionHostSemaphore = new SemaphoreSlim(1, 1);

        private static async Task<bool> WaitUntilReady(HttpClient client)
        {
            for (var limit = 0; limit < 30; limit++)
            {
                try
                {
                    var response = await client.GetAsync("/admin/host/ping");
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    await Task.Delay(1000);
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
            return false;
        }

        public static class TestResourceManager
        {
            private static SemaphoreSlim _networkSemaphore = new SemaphoreSlim(3, 3); // Allow 3 concurrent tests

            public static async Task<IDisposable> AcquireNetworkResourceAsync()
            {
                await _networkSemaphore.WaitAsync();
                return new DisposableAction(() => _networkSemaphore.Release());
            }
        }
        public class DisposableAction : IDisposable
        {
            private readonly Action _action;
            private bool _disposed = false;

            public DisposableAction(Action action)
            {
                _action = action ?? throw new ArgumentNullException(nameof(action));
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _action();
                    _disposed = true;
                }
            }
        }


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

                    funcProcess.Kill();

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
            }), fileWriter);

            /*
            await RetryHelper.ExecuteAsyncWithRetry(async () =>
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

                    try
                    {
                        // Try ping endpoint as a fallback
                        var pingResponse = await httpClient.GetAsync($"{url}/admin/host/ping");
                        LogMessage($"Ping response: {pingResponse.StatusCode}");
                        fileWriter?.Flush();
                        if (pingResponse.IsSuccessStatusCode)
                        {
                            LogMessage("Host responded to ping - assuming it's running");
                            return true;
                        }
                    }
                    catch { }

                    return false;
                }
                catch (Exception ex)
                {
                    LogMessage($"Error checking host status: {ex.Message}");
                    return false;
                }
            }, shouldStopRetry: result => result == true, 10, () => { return RetryHelper.TestingIntervals.Select(Task.Delay);  }, fileWriter);
            */


            /*
            await RetryHelper.RetryUntilTimeoutAsync(async () =>
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

                    try
                    {
                        // Try ping endpoint as a fallback
                        var pingResponse = await httpClient.GetAsync($"{url}/admin/host/ping");
                        LogMessage($"Ping response: {pingResponse.StatusCode}");
                        fileWriter?.Flush();
                        if (pingResponse.IsSuccessStatusCode)
                        {
                            LogMessage("Host responded to ping - assuming it's running");
                            return true;
                        }
                    }
                    catch { }

                    return false;
                }
                catch (Exception ex)
                {
                    LogMessage($"Error checking host status: {ex.Message}");
                    return false;
                }
            }, fileWriter, timeout);
            */
        }

        public static bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // If we get here, the file is not locked
                    return false;
                }
            }
            catch (IOException)
            {
                // The file is locked by another process
                return true;
            }
            catch (Exception)
            {
                // Another error occurred
                return false;
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

        public static async Task<string> ProcessStartedHandlerHelper(int port, Process process,
    StreamWriter? fileWriter, string functionCall = "")
        {
            string capturedContent = "";
            try
            {
                fileWriter.WriteLine("[HANDLER] Starting process started handler helper");
                fileWriter.Flush();

                fileWriter.WriteLine($"[HANDLER] Process working directory: {process.StartInfo.WorkingDirectory}");

                await WaitForFunctionHostToStart(process, port, fileWriter);

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

        public static async Task<FuncStartCommand> WaitTillHostHasStarted(FuncStartCommand funcStartCommand, int port, string functionCall = "", string capturedContent = "", string because = "")
        {
            string outputFromFunction = "";

            funcStartCommand.CommandOutputHandler = async output =>
            {
                outputFromFunction += output;
            };

            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
            {
                fileWriter?.WriteLine("[HANDLER] Handler started at " + DateTime.Now);
                fileWriter?.Flush();

                try
                {
                    int retryCount = 1;

                    await RetryHelper.RetryUntilTimeoutAsync(() =>
                    {
                        fileWriter?.WriteLine("Current retry count: " + retryCount);
                        fileWriter?.Flush();

                        retryCount += 1;
                        if (outputFromFunction.Contains("Host started"))
                        {
                            fileWriter?.WriteLine("Host has started");
                            fileWriter?.Flush();
                            return Task.FromResult(true);
                        }
                        fileWriter?.WriteLine("Returning false");
                        fileWriter?.Flush();
                        return Task.FromResult(false);
                    }, fileWriter);

                    if (!string.IsNullOrEmpty(functionCall))
                    {
                        fileWriter?.WriteLine($"[HANDLER] Making request to http://localhost:{port}/api/{functionCall}");
                        fileWriter?.Flush();

                        using (var client = new HttpClient())
                        {
                            var response = await client.GetAsync($"http://localhost:{port}/api/{functionCall}");
                            var responseContent = await response.Content.ReadAsStringAsync();

                            fileWriter?.WriteLine($"[HANDLER] Received response: {responseContent}");
                            fileWriter?.Flush();
                            responseContent.Should().Be(capturedContent);
                        }
                    }
                }
                catch (ApplicationException ex)
                {
                    fileWriter?.WriteLine("[HANDLER] Handler ran into an exception at " + DateTime.Now);
                    fileWriter?.WriteLine("[HANDLER] Handler ran into an exception: " + ex.Message);
                    fileWriter?.Flush();
                    throw new TimeoutException("Host was not started in alloted time");
                }
                finally
                {
                    fileWriter?.WriteLine("[HANDLER] Process is going to be killed");
                    fileWriter?.Flush();
                    process.Kill();
                }
            };
            return funcStartCommand;
        }
    }

    
}
