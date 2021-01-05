using Azure.Functions.Cli.Kubernetes.KEDA.V1;
using Azure.Functions.Cli.Kubernetes.KEDA.V2;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using System;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public class KedaResourceFactory
    {
        private readonly KedaVersion? _kedaVersion;

        public KedaResourceFactory(KedaVersion? kedaVersion)
        {
            _kedaVersion = kedaVersion;
        }

        public IKubernetesResource Create(string name, string @namespace, TriggersPayload triggers,
               DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas)
        {
            IKedaResource kedaResource;
            switch (_kedaVersion)
            {
                case KedaVersion.v1:
                    kedaResource = new KedaV1Resource();
                    break;
                case KedaVersion.v2:
                    kedaResource = new KedaV2Resource();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_kedaVersion), _kedaVersion, "Specified KEDA version is not supported");
            }

            return kedaResource.GetKubernetesResource(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod, minReplicas, maxReplicas);
        }
    }
}
