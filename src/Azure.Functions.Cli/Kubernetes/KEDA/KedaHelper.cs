using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Kubernetes.KEDA.V1;
using Azure.Functions.Cli.Kubernetes.KEDA.V2;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Newtonsoft.Json.Linq;

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

        internal static async Task<IKubernetesResource> GetScaledObject(string name, string @namespace, TriggersPayload triggers, DeploymentV1Apps deployment, int? pollingInterval, int? cooldownPeriod, int? minReplicas, int? maxReplicas, KedaVersion? kedaVersion = null)
        {
            kedaVersion = kedaVersion ?? await DetermineCurrentVersion(@namespace);
            KedaResourceFactory kedaResourceFactory = new KedaResourceFactory(kedaVersion);
            return kedaResourceFactory.Create(name, @namespace, triggers, deployment, pollingInterval, cooldownPeriod, minReplicas, maxReplicas);
        }

        public static async Task<KedaVersion> DetermineCurrentVersion(string @namespace)
        {
            // Get KEDA resource information
            var resourceInfo = await KubernetesHelper.ResourceExists("Deployment", "keda-metrics-apiserver", @namespace, returnJsonOutput: true);
            if (resourceInfo.ResourceExists == false)
            {
                // We use KEDA v2 as a default
                return KedaVersion.v2;
            }
            
            var parsedJson = JToken.Parse(resourceInfo.Output);	
            // TODO: Interpret app.kubernetes.io/version label	

            return KedaVersion.v1;
        }
    }
}
