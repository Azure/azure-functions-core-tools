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

        public static async Task WaitForFunctionHostToStart(
         Process funcProcess,
         int port,
         StreamWriter? fileWriter = null,
         string? functionCall = null,
         int timeout = 120 * 1000,
         HttpStatusCode expectedStatus = HttpStatusCode.OK)
        {
            var url = $"{FunctionsHostUrl}:{port.ToString()}";
            using var httpClient = new HttpClient();

            void LogMessage(string message)
            {
                Console.WriteLine(message);
                fileWriter?.WriteLine($"[HOST STATUS] {message}");
            }

            LogMessage($"Starting to wait for function host on {url} at {DateTime.Now}");
            fileWriter?.Flush();
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
                        throw new InvalidOperationException($"Process exited with code {funcProcess.ExitCode}");
                    }

                    /*
                    var response = await httpClient.GetAsync($"{url}/admin/host/status");

                    LogMessage($"Response status code: {response.StatusCode}");
                    fileWriter?.Flush();


                    // If we're expecting a 401, check for that first
                    if (expectedStatus == HttpStatusCode.Unauthorized && response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        LogMessage($"Received expected 401 Unauthorized response - host is ready");
                        return true;
                    }
                    

                    // For successful responses, check the running state
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        LogMessage($"Host status response: {content}");

                        try
                        {
                            var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.TryGetProperty("state", out JsonElement value) &&
                                value.GetString() == "Running")
                            {
                                LogMessage("Host is in Running state - ready to process requests");
                                return true;
                            }
                        }
                        catch (JsonException ex)
                        {
                            LogMessage($"Error parsing JSON: {ex.Message}");
                        }
                    }
                    */
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

                    // Try the function endpoint directly as a desperate measure
                    if (!string.IsNullOrEmpty(functionCall))
                    {
                        try
                        {
                            var functionResponse = await httpClient.GetAsync($"{url}/api/{functionCall}");
                            LogMessage($"Functions status code: {functionResponse.StatusCode}");
                            fileWriter?.Flush();

                            if (functionResponse.IsSuccessStatusCode)
                            {
                                LogMessage("Function endpoint responded - assuming host is running");
                                return true;
                            }
                        }
                        catch { }
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    LogMessage($"Error checking host status: {ex.Message}");
                    return false;
                }
            }, timeout);
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

        public static async Task ProcessStartedHandlerHelper(int port, Process process, ITestOutputHelper log,
    StreamWriter? fileWriter, string functionCall = "", string capturedContent = "")
        {
            try
            {
                log.WriteLine("Waiting for host to start");
                fileWriter?.WriteLine("[HANDLER] Waiting for host to start");
                fileWriter?.Flush();

                await WaitForFunctionHostToStart(process, port, fileWriter, functionCall);

                log.WriteLine("Host started");
                fileWriter?.WriteLine("[HANDLER] Host started");
                fileWriter?.Flush();

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
            catch (Exception e)
            {
                log.WriteLine("Error was thrown: " + e.ToString());
                fileWriter?.WriteLine("[HANDLER-ERROR] " + e.ToString());
                fileWriter?.Flush();
            }
            finally
            {
                log.WriteLine("Process is going to be killed");
                fileWriter?.WriteLine("[HANDLER] Process is going to be killed");
                fileWriter?.Flush();
                process.Kill(true);
            }
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
                            return true;
                        }
                        fileWriter?.WriteLine("Returning false");
                        fileWriter?.Flush();
                        return false;
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
