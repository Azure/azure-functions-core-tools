using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm
{
    internal class ArmClient
    {
        private readonly int _retryCount;
        private readonly IArmTokenManager _tokenManager;
        private readonly string _currentTenant;
        private readonly Random _random;

        public ArmClient(IArmTokenManager tokenManager, string currentTenant, int retryCount)
        {
            _retryCount = retryCount;
            _tokenManager = tokenManager;
            _currentTenant = currentTenant;
            _random = new Random();
        }

        public Task<HttpResponseMessage> HttpInvoke(HttpMethod method, Uri uri, object objectPayload = null)
        {
            return HttpInvoke(method.Method, uri, objectPayload);
        }

        public async Task<HttpResponseMessage> HttpInvoke(string method, Uri uri, object objectPayload = null)
        {
            var socketTrials = 10;
            var retries = this._retryCount;
            while (true)
            {
                try
                {
                    var response = await HttpInvoke(uri, method, objectPayload);

                    if (!response.IsSuccessStatusCode && this._retryCount > 0)
                    {
                        while (retries > 0)
                        {
                            response = await HttpInvoke(uri, method, objectPayload);
                            if (response.IsSuccessStatusCode)
                            {
                                return response;
                            }
                            else
                            {
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

        private async Task<HttpResponseMessage> HttpInvoke(Uri uri, string verb, object objectPayload)
        {
            var payload = JsonConvert.SerializeObject(objectPayload);
            using (var client = new HttpClient(new HttpClientHandler()))
            {
                const string jsonContentType = "application/json";
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {await _tokenManager.GetToken(_currentTenant)}");
                client.DefaultRequestHeaders.Add("User-Agent", "functions-cli/2.0");
                client.DefaultRequestHeaders.Add("Accept", jsonContentType);
                client.DefaultRequestHeaders.Add("x-ms-request-id", Guid.NewGuid().ToString());

                HttpResponseMessage response = null;
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