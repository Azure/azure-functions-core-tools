
using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Helpers
{
    public static class HostHelpers
    {
        public static async Task<string> GetCustomHandlerExecutable()
        {
            if (!FileSystemHelpers.FileExists(Constants.HostJsonFileName))
            {
                throw new InvalidOperationException();
            }

            var hostJson = JsonConvert.DeserializeObject<JToken>(await FileSystemHelpers.ReadAllTextFromFileAsync(Constants.HostJsonFileName));
            return hostJson["customHandler"]?["description"]?["defaultExecutablePath"]?.ToString() ?? string.Empty;
        }
    }
}