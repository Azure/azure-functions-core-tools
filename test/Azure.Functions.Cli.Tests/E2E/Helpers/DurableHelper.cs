using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public static class DurableHelper
    {
        public static void SetTaskHubName(string workingDirectoryPath, string taskHubName)
        {
            string hostJsonPath = Path.Combine(workingDirectoryPath, ScriptConstants.HostMetadataFileName);
            if (File.Exists(hostJsonPath))
            {
                // Attempt to retrieve Durable override settings from host.json
                dynamic hostSettings = JObject.Parse(File.ReadAllText(hostJsonPath));

                string version = hostSettings["version"];
                if (version?.Equals("2.0") == true)
                {
                    // If the version is (explicitly) 2.0, prepend path to 'durableTask' with 'extensions'
                    hostSettings["extensions"]["durableTask"]["HubName"] = taskHubName;
                }
                else
                {
                    hostSettings["durableTask"]["HubName"] = taskHubName;
                }

                string output = JsonConvert.SerializeObject(hostSettings, Formatting.Indented);
                File.WriteAllText(hostJsonPath, output);
            }
        }
    }
}
