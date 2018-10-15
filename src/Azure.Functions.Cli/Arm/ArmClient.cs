using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Newtonsoft.Json;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Arm
{
    public static class ArmClient
    {
        private static readonly Random _random;

        static ArmClient()
        {
            _random = new Random();
        }

        public static Task<HttpResponseMessage> HttpInvoke(HttpMethod method, Uri uri, string accessToken, object objectPayload = null, int retryCount = 3)
        {
            return HttpInvoke(method.Method, uri, accessToken, objectPayload, retryCount);
        }

        public static async Task<HttpResponseMessage> HttpInvoke(string method, Uri uri, string accessToken, object objectPayload = null, int retryCount = 3)
        {
            var socketTrials = 10;
            var retries = retryCount;
            while (true)
            {
                try
                {
                    var response = await HttpInvoke(uri, method, accessToken, objectPayload);

                    if (!response.IsSuccessStatusCode && retryCount > 0)
                    {
                        while (retries > 0)
                        {
                            response = await HttpInvoke(uri, method, accessToken, objectPayload);
                            if (response.IsSuccessStatusCode)
                            {
                                return response;
                            }
                            else
                            {
                                if (StaticSettings.IsDebug)
                                {
                                    ColoredConsole.Error.WriteLine(ErrorColor(await response.Content.ReadAsStringAsync()));
                                }
                                retries--;
                            }
                        }
                    }
                    return response;
                }
                catch (SocketException)
                {
                    if (socketTrials <= 0) throw;
                    socketTrials--;
                }
                catch (Exception)
                {
                    if (retries <= 0) throw;
                    retries--;
                }
                await Task.Delay(_random.Next(1000, 10000));
            }
        }

        private static async Task<HttpResponseMessage> HttpInvoke(Uri uri, string verb, string accessToken, object objectPayload)
        {
            var payload = JsonConvert.SerializeObject(objectPayload);
            using (var client = new HttpClient(new HttpClientHandler()))
            {
                const string jsonContentType = "application/json";
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                client.DefaultRequestHeaders.Add("User-Agent", "functions-cli/2.0");
                client.DefaultRequestHeaders.Add("Accept", jsonContentType);
                client.DefaultRequestHeaders.Add("x-ms-request-id", Guid.NewGuid().ToString());

                HttpResponseMessage response = null;
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(VerboseColor($"{verb.ToUpper()} {uri.ToString()}"));
                }

                if (String.Equals(verb, "get", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.GetAsync(uri).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.DeleteAsync(uri).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "post", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.PostAsync(uri, new StringContent(payload ?? String.Empty, Encoding.UTF8, jsonContentType)).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "put", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.PutAsync(uri, new StringContent(payload ?? String.Empty, Encoding.UTF8, jsonContentType)).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "patch", StringComparison.OrdinalIgnoreCase))
                {
                    using (var message = new HttpRequestMessage(new HttpMethod("PATCH"), uri))
                    {
                        message.Content = new StringContent(payload ?? String.Empty, Encoding.UTF8, jsonContentType);
                        response = await client.SendAsync(message).ConfigureAwait(false);
                    }
                }
                else
                {
                    throw new InvalidOperationException(String.Format("Invalid http verb '{0}'!", verb));
                }

                return response;
            }
        }
    }
}