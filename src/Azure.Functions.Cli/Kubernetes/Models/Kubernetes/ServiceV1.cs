using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ServiceV1 : BaseKubernetesResource<ServiceSpecV1>
    { }

    public class ServiceSpecV1 : IKubernetesSpec
    {
        [JsonProperty("selector")]
        public IDictionary<string, string> Selector { get; set; }

        [JsonProperty("ports")]
        public IEnumerable<ServicePortV1> Ports { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }

    public class ServicePortV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("protocol")]
        public string Protocol { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("targetPort")]
        public int TargetPort { get; set; }
    }
}