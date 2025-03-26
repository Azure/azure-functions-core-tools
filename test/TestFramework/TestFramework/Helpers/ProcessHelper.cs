using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TestFramework.Helpers
{
    public class ProcessHelper
    {
        private static string FunctionsHostUrl = "http://localhost";

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

        public static async Task<string> ProcessStartedHandlerHelper(int port, Process process, string functionCall = "")
        {
            string capturedContent = "";
            try
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, port);

                if (!string.IsNullOrEmpty(functionCall))
                {
                    using (var client = new HttpClient())
                    {
                        var response = await client.GetAsync($"http://localhost:{port}/api/{functionCall}");
                        capturedContent = await response.Content.ReadAsStringAsync();
                    }
                }
            }
            finally
            {
                process.Kill(true);
            }
            return capturedContent;
        }
    }
}
