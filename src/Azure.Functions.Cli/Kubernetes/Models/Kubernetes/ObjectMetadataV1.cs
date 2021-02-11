
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ObjectMetadataV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("namespace")]
        public string Namespace { get; set; }

        [JsonProperty("labels")]
        public IDictionary<string, string> Labels { get; set; }

        [JsonProperty("annotations")]
        public IDictionary<string, string> Annotations { get; set; }
    }
}