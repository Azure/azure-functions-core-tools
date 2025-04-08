using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Extensions
{
    internal static class UriExtensions
    {
        public static async Task<bool> IsServerRunningAsync(this Uri server)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var rootResponse = await client.GetAsync(server);
                    return rootResponse.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
