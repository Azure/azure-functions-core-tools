using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.Models
{
    public class TriggersPayload
    {
        [JsonProperty("hostJson")]
        public JObject HostJson { get; set; }

        [JsonProperty("functionsJson")]
        public IDictionary<string, JObject> FunctionsJson { get; set; }
    }
}