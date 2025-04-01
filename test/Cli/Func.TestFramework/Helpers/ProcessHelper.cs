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

        public static async Task WaitForFunctionHostToStart(Process funcProcess, int port, StreamWriter fileWriter = null, int timeout = 120 * 1000, HttpStatusCode expectedStatus = HttpStatusCode.OK)
        {
            var url = $"{FunctionsHostUrl}:{port.ToString()}";
            using var httpClient = new HttpClient();

            void Log(string message)
            {
                Console.WriteLine(message);
                fileWriter?.WriteLine($"[HOST STATUS] {message}");
            }

            Log($"Starting to wait for function host on {url}");

            await RetryHelper.RetryAsync(async () =>
            {
                try
                {
                    var response = await httpClient.GetAsync($"{url}/admin/host/status");

                    // If we're expecting a 401, check for that first
                    if (expectedStatus == HttpStatusCode.Unauthorized && response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Log($"Received expected 401 Unauthorized response - host is ready");
                        return true;
                    }

                    // For successful responses, check the running state
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        Log($"Host status response: {content}");

                        try
                        {
                            var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.TryGetProperty("state", out JsonElement value) &&
                                value.GetString() == "Running")
                            {
                                Log("Host is in Running state - ready to process requests");
                                return true;
                            }
                        }
                        catch (JsonException ex)
                        {
                            Log($"Error parsing JSON: {ex.Message}");
                        }
                    }

                    Log($"Host not ready yet. Status: {response.StatusCode}");
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"Error checking host status: {ex.Message}");
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

        public static async Task ProcessStartedHandlerHelper(int port, Process process, ITestOutputHelper log, string testName = "", string functionCall = "", string capturedContent = "")
        {
            // Set up file logging
            var directoryToLogTo = Environment.GetEnvironmentVariable("DIRECTORY_TO_LOG_TO");
            if (string.IsNullOrEmpty(directoryToLogTo))
            {
                directoryToLogTo = Directory.GetCurrentDirectory();
            }

            // Ensure directory exists
            Directory.CreateDirectory(directoryToLogTo);

            // Create unique log file name
            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string logFilePath = Path.Combine(directoryToLogTo,
                $"process_started_handler_{testName}_{DateTime.Now:yyyyMMdd_HHmmss}_{uniqueId}.log");

            // Set up file writer
            StreamWriter fileWriter = null;
            try
            {
                // Create file stream with sharing mode
                var fileStream = new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                fileWriter = new StreamWriter(fileStream)
                {
                    AutoFlush = true // Ensure content is written immediately
                };

                // Write initial information
                fileWriter.WriteLine($"=== Process Handler Started at {DateTime.Now} ===");
                fileWriter.WriteLine($"Test Name: {testName}");
                fileWriter.WriteLine($"Port: {port}");
                fileWriter.WriteLine($"Function Call: {functionCall}");
                fileWriter.WriteLine($"Expected Content: {capturedContent}");
                fileWriter.WriteLine($"Process ID: {process.Id}");
                fileWriter.WriteLine("====================================");

                // Set up output and error capturing
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                // Start asynchronous reading of stdout and stderr
                var outputTask = Task.Run(() => {
                    try
                    {
                        string line;
                        while ((line = process.StandardOutput.ReadLine()) != null)
                        {
                            var logLine = $"[STDOUT] {line}";
                            log.WriteLine(logLine);
                            fileWriter.WriteLine(logLine);
                            outputBuilder.AppendLine(line);
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"Error reading stdout: {ex.Message}";
                        log.WriteLine(errorMsg);
                        fileWriter.WriteLine(errorMsg);
                    }
                });

                var errorTask = Task.Run(() => {
                    try
                    {
                        string line;
                        while ((line = process.StandardError.ReadLine()) != null)
                        {
                            var logLine = $"[STDERR] {line}";
                            log.WriteLine(logLine);
                            fileWriter.WriteLine(logLine);
                            errorBuilder.AppendLine(line);
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"Error reading stderr: {ex.Message}";
                        log.WriteLine(errorMsg);
                        fileWriter.WriteLine(errorMsg);
                    }
                });

                // Log start of host wait
                var waitMsg = "Waiting for host to start";
                log.WriteLine(waitMsg);
                fileWriter.WriteLine(waitMsg);

                await WaitForFunctionHostToStart(process, port, fileWriter);

                // Log successful host start
                var startedMsg = "Host started successfully";
                log.WriteLine(startedMsg);
                fileWriter.WriteLine(startedMsg);

                if (!string.IsNullOrEmpty(functionCall))
                {
                    var requestMsg = $"Making request to http://localhost:{port}/api/{functionCall}";
                    log.WriteLine(requestMsg);
                    fileWriter.WriteLine(requestMsg);

                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync($"http://localhost:{port}/api/{functionCall}");
                        var responseContent = await response.Content.ReadAsStringAsync();

                        var responseMsg = $"HTTP Response: {response.StatusCode}, Content: '{responseContent}'";
                        log.WriteLine(responseMsg);
                        fileWriter.WriteLine(responseMsg);

                        // Verify expected response
                        responseContent.Should().Be(capturedContent);
                    }
                }

                // Log successful completion
                fileWriter.WriteLine("====================================");
                fileWriter.WriteLine("Process handler completed successfully");
            }
            catch (Exception e)
            {
                // Log the exception
                var errorMsg = "Error was thrown: " + e.ToString();
                log.WriteLine(errorMsg);

                if (fileWriter != null)
                {
                    fileWriter.WriteLine("====================================");
                    fileWriter.WriteLine(errorMsg);

                    // Include stdout and stderr output to help diagnose
                    fileWriter.WriteLine("====================================");
                    fileWriter.WriteLine("Last Process Output:");

                    try
                    {
                        if (process.StandardOutput.Peek() > -1)
                        {
                            var output = process.StandardOutput.ReadToEnd();
                            fileWriter.WriteLine("--- STDOUT ---");
                            fileWriter.WriteLine(output);
                        }
                    }
                    catch { }

                    try
                    {
                        if (process.StandardError.Peek() > -1)
                        {
                            var error = process.StandardError.ReadToEnd();
                            fileWriter.WriteLine("--- STDERR ---");
                            fileWriter.WriteLine(error);
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                // Log process termination
                var killMsg = "Process is going to be killed";
                log.WriteLine(killMsg);

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        fileWriter?.WriteLine("Process was killed");
                    }
                    else
                    {
                        fileWriter?.WriteLine($"Process already exited with code: {process.ExitCode}");
                    }
                }
                catch (Exception ex)
                {
                    log.WriteLine($"Error killing process: {ex.Message}");
                }
                if (fileWriter != null)
                {
                    fileWriter.WriteLine("====================================");
                    fileWriter.WriteLine(killMsg);
                    fileWriter.WriteLine($"=== Process Handler Ended at {DateTime.Now} ===");

                    // Close and dispose the file writer
                    try
                    {
                        fileWriter.Close();
                        fileWriter.Dispose();
                    }
                    catch (Exception ex)
                    {
                        log.WriteLine($"Error closing log file: {ex.Message}");
                    }
                }
            }
        }
    }
}
