
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ScaledObjectV1Alpha1 : BaseKubernetesResource<ScaledObjectSpecV1Alpha1>
    {
    }

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

    public class ScaledObjectScaleTargetRefV1Alpha1
    {
        [JsonProperty("deploymentName")]
        public string DeploymentName { get; set; }
    }

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