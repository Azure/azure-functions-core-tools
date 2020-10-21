using System;
using Azure.Functions.Cli.Kubernetes.KEDA.V1;
using Azure.Functions.Cli.Kubernetes.KEDA.V2;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;

namespace Azure.Functions.Cli.Kubernetes.KEDA
{
    public static class KedaHelper
    {
        internal static string GetKedaDeploymentYaml(string @namespace, KedaVersion kedaVersion = KedaVersion.v2)
        {
            string rawTemplate;
            switch (kedaVersion)
            {
                case KedaVersion.v1:
                    rawTemplate = StaticResources.KedaV1Template.Result;
                    break;
                case KedaVersion.v2:
                    rawTemplate = StaticResources.KedaV2Template.Result;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kedaVersion), kedaVersion, "Specified KEDA version is not supported");
            }

            return rawTemplate.Replace("KEDA_NAMESPACE", @namespace);
        }

        internal static IKubernetesResource GetScaledObject(string name, string @namespace, TriggersPayload triggers, DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas, KedaVersion? kedaVersion = null)
        {
            kedaVersion = kedaVersion ?? DetermineCurrentVersion(@namespace);

            switch (kedaVersion)
            {
                case KedaVersion.v1:
                    return KedaV1ResourceFactory.GetScaledObject(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod, minReplicas, maxReplicas);
                case KedaVersion.v2:
                    return KedaV2ResourceFactory.GetScaledObject(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod, minReplicas, maxReplicas);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kedaVersion), kedaVersion, "Specified KEDA version is not supported");
            }
        }

        public static KedaVersion DetermineCurrentVersion(string @namespace)
        {
            // TODO: discover version
            return KedaVersion.v2;
        }
    }
}
