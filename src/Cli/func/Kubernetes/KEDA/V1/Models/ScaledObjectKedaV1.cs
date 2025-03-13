using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;

namespace Azure.Functions.Cli.Kubernetes.KEDA.V1.Models
{
    public class ScaledObjectKedaV1 : BaseKubernetesResource<ScaledObjectSpecV1Alpha1>
    {
        public override string ApiVersion => "keda.k8s.io/v1alpha1";
        public override string Kind => "ScaledObject";
    }
}