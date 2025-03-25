using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;

namespace Azure.Functions.Cli.Kubernetes.Models
{
    internal class DurableTaskConfig
    {
        [JsonProperty("maxConcurrentOrchestratorFunctions")]
        public int MaxConcurrentOrchestratorFunctions { get; set; } = 10;

        [JsonProperty("maxConcurrentActivityFunctions")]
        public int MaxConcurrentActivityFunctions { get; set; } = 1;

        [JsonProperty("storageProvider")]
        public JObject StorageProvider { get; set; }
    }
}
