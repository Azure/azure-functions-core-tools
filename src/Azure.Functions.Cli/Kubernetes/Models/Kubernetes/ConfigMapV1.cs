
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ConfigMapV1 : BaseKubernetesResource<IKubernetesSpec>
    {
        [JsonProperty("data")]
        public IDictionary<string, string> Data { get; set; }
    }
}