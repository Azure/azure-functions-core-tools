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

namespace Func.TestFramework.Helpers
{
    public class ProcessHelper
    {
        private static string FunctionsHostUrl = "http://localhost";

        private async Task<bool> WaitUntilReady(HttpClient client)
        {
            for (var limit = 0; limit < 10; limit++)
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

        public static async Task WaitForFunctionHostToStart(Process funcProcess, int port, int timeout = 120 * 1000, HttpStatusCode expectedStatus = HttpStatusCode.OK)
        {
            var url = $"{FunctionsHostUrl}:{port.ToString()}";
            using var httpClient = new HttpClient();

            await RetryHelper.RetryAsync(async () =>
            {
                try
                {
                    var response = await httpClient.GetAsync($"{url}/admin/host/status");

                    // If we're expecting a 401, check for that first
                    if (expectedStatus == HttpStatusCode.Unauthorized && response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Console.WriteLine($"Received expected 401 Unauthorized response - host is ready");
                        return true;
                    }

                    // For successful responses, check the running state
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"Host status response: {content}");

                        try
                        {
                            var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.TryGetProperty("state", out JsonElement value) &&
                                value.GetString() == "Running")
                            {
                                return true;
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"Error parsing JSON: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"Host not ready yet. Status: {response.StatusCode}");
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error checking host status: {ex.Message}");
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

        public static async Task ProcessStartedHandlerHelper(int port, Process process, ITestOutputHelper log, string functionCall = "", string capturedContent = "")
        {
            // Set up continuous reading of stdout
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();

            // Start asynchronous reading of stdout and stderr
            var outputTask = Task.Run(() => {
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    log.WriteLine($"STDOUT: {line}");
                    outputBuilder.AppendLine(line);
                }
            });

            var errorTask = Task.Run(() => {
                string line;
                while ((line = process.StandardError.ReadLine()) != null)
                {
                    log.WriteLine($"STDERR: {line}");
                    errorBuilder.AppendLine(line);
                }
            });

            try
            {
                log.WriteLine("Waiting for host to start");

                // Set a timeout for the host startup
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5)); // 5 minute timeout
                var startTask = WaitForFunctionHostToStart(process, port);

                // Wait for either success or timeout
                var completedTask = await Task.WhenAny(startTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    log.WriteLine("TIMEOUT: Host did not start within 5 minutes");
                    log.WriteLine("Current stdout content:");
                    log.WriteLine(outputBuilder.ToString());
                    throw new TimeoutException("Host did not start within the timeout period");
                }

                log.WriteLine("Host started");

                if (!string.IsNullOrEmpty(functionCall))
                {
                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync($"http://localhost:{port}/api/{functionCall}");
                        var responseContent = await response.Content.ReadAsStringAsync();
                        log.WriteLine($"HTTP Response: {responseContent}");
                        responseContent.Should().Be(capturedContent);
                    }
                }
            }
            catch (Exception e)
            {
                log.WriteLine("Error was thrown: " + e.ToString());

                // Log the captured output so far to help diagnose the issue
                log.WriteLine("===== CAPTURED STDOUT =====");
                log.WriteLine(outputBuilder.ToString());
                log.WriteLine("===== CAPTURED STDERR =====");
                log.WriteLine(errorBuilder.ToString());

                throw; // Rethrow to fail the test
            }
            finally
            {
                log.WriteLine("Process is going to be killed");

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        log.WriteLine("Process was killed");
                    }
                    else
                    {
                        log.WriteLine($"Process already exited with code: {process.ExitCode}");
                    }
                }
                catch (Exception ex)
                {
                    log.WriteLine($"Error killing process: {ex.Message}");
                }

                // Save full stdout to a file for later analysis
                string logFileName = $"process_output_{port}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                string logFilePath = Path.Combine(Path.GetTempPath(), logFileName);

                try
                {
                    File.WriteAllText(logFilePath, outputBuilder.ToString());
                    log.WriteLine($"Full stdout saved to: {logFilePath}");
                }
                catch (Exception ex)
                {
                    log.WriteLine($"Error saving stdout to file: {ex.Message}");
                }
            }
        }
    }
}
