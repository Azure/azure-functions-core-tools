using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V2.Models
{
    public class ScaledObjectScaleTargetRefV1Alpha1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("envSourceContainerName")]
        public string EnvSourceContainerName { get; set; }
    }
}
