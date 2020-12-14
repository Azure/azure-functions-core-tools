using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public class ScaledObjectScaleTargetRefV1Alpha1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("envSourceContainerName")]
        public string EnvSourceContainerName { get; set; }
    }
}
