using System.Collections.Generic;
using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.Models;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V2.Models
{
    public class ScaledObjectSpecV1Alpha1 : IKubernetesSpec
    {
        [JsonProperty("scaleTargetRef")]
        public ScaledObjectScaleTargetRefV1Alpha1 ScaleTargetRef { get; set; }

        [JsonProperty("pollingInterval")]
        public int? PollingInterval { get; set; }

        [JsonProperty("cooldownPeriod")]
        public int? CooldownPeriod { get; set; }

        [JsonProperty("minReplicaCount")]
        public int? MinReplicaCount { get; set; }

        [JsonProperty("maxReplicaCount")]
        public int? MaxReplicaCount { get; set; }

        [JsonProperty("triggers")]
        public IEnumerable<ScaledObjectTriggerV1Alpha1> Triggers { get; internal set; }
    }
}
