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
            try
            {
                log.WriteLine("Waiting for host to start");

                if (!string.IsNullOrEmpty(functionCall))
                {
                    using (var client = new HttpClient())
                    {
                        await WaitUntilReady(client);
                        var response = await client.GetAsync($"http://localhost:{port}/api/{functionCall}");
                        var responseContent = await response.Content.ReadAsStringAsync();

                        responseContent.Should().Be(capturedContent);
                    }
                }
            }
            catch (Exception e)
            {
                log.WriteLine("Error was thrown: " + e.ToString());
            }
            finally
            {
                log.WriteLine("Process is going to be killed");
                process.Kill(true);
            }
        }
    }
}
