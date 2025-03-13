using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ServiceV1 : BaseKubernetesResource<ServiceSpecV1>
    {
        [JsonProperty("status")]
        public ServiceStatus Status { get; set; }
    }

    public class ServiceSpecV1 : IKubernetesSpec
    {
        [JsonProperty("selector")]
        public IDictionary<string, string> Selector { get; set; }

        [JsonProperty("ports")]
        public IEnumerable<ServicePortV1> Ports { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("clusterIP", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ClusterIp { get; set; }
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

    public class ServiceStatus
    {
        [JsonProperty("loadBalancer")]
        public ServiceLoadBalancer LoadBalancer { get; set; }
    }

    public class ServiceLoadBalancer
    {
        [JsonProperty("ingress")]
        public IEnumerable<ServiceIp> Ingress { get; set; }
    }

    public class ServiceIp
    {
        [JsonProperty("ip")]
        public string Ip { get; set; }
    }
}