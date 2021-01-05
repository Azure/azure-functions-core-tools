using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V1.Models
{
    public class ScaledObjectScaleTargetRefV1Alpha1
    {
        [JsonProperty("deploymentName")]
        public string DeploymentName { get; set; }
    }
}
