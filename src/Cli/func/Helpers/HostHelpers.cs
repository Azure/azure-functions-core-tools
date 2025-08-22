// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Helpers
{
    public static class HostHelpers
    {
        public static async Task<string> GetCustomHandlerExecutable(string path = null)
        {
            var file = !string.IsNullOrEmpty(path) ? Path.Combine(path, Constants.HostJsonFileName) : Constants.HostJsonFileName;
            if (!FileSystemHelpers.FileExists(file))
            {
                throw new InvalidOperationException();
            }

            var hostJson = JsonConvert.DeserializeObject<JToken>(await FileSystemHelpers.ReadAllTextFromFileAsync(file));
            return hostJson["customHandler"]?["description"]?["defaultExecutablePath"]?.ToString() ?? string.Empty;
        }
    }
}
