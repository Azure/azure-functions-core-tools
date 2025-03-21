using Azure.Functions.Cli.Kubernetes.KEDA.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V2.Models
{
    public class ScaledObjectKedaV2 : BaseKubernetesResource<ScaledObjectSpecV1Alpha1>
    {
        public override string ApiVersion => "keda.sh/v1alpha1";
        public override string Kind => "ScaledObject";
    }
}