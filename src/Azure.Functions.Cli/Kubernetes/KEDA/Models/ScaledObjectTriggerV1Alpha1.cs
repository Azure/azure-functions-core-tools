using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public class ScaledObjectTriggerV1Alpha1
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("metadata")]
        public IDictionary<string, string> Metadata { get; set; }
    }
}
