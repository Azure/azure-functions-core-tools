using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    internal class VersionHelper
    {

        // Download content from https://functionscdn.azureedge.net/public/cli-feed-v4.json and return as string with timeout of two seconds
        public static async Task<string> GetLatestVersionAsync()
        {
            try
            {
                var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync("https://functionscdn.azureedge.net/public/cli-feed-v4.json");
                var content = await response.Content.ReadAsStringAsync();
                dynamic data = JsonConvert.DeserializeObject(content);
                dynamic releases = (IEnumerable) data.releases;
                return content;
            }
            catch (Exception e)
            {
                return null;
            }

            
        }

    }
}
