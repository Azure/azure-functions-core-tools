using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Kubernetes.Models;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Colors.Net;
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
            ColoredConsole.WriteLine("Determining current KEDA version");
            var resourceInfo = await GetKedaDeployment(@namespace);
            if (resourceInfo.ResourceExists == false)
            {
                ColoredConsole.WriteLine("KEDA was not found, using KEDA v2 as default");

                // We use KEDA v2 as a default
                return KedaVersion.v2;
            }

            var parsedJson = JToken.Parse(resourceInfo.Output);
            var rawKedaVersion = parsedJson["metadata"]?["labels"]?["app.kubernetes.io/version"]?.ToString();
            
            if (string.IsNullOrWhiteSpace(rawKedaVersion) == false && rawKedaVersion.StartsWith("2."))
            {
                return KedaVersion.v2;
            }

            // We were unable to determine the version, falling back to v1
            return KedaVersion.v1;
        }

        public static async Task<bool> IsInstalled(string @namespace)
        {
            var resourceInfo = await GetKedaDeployment(@namespace);
            return resourceInfo.ResourceExists;
        }

        private static async Task<(string Output, bool ResourceExists)> GetKedaDeployment(string @namespace)
        {
            // Attempt to look for v2 resource first
            var resourceInfo = await KubernetesHelper.ResourceExists("Deployment", "keda-operator", @namespace, returnJsonOutput: true);
            if (resourceInfo.ResourceExists == false)
            {
                // As a fallback, look for v1 resource for backwards compatibility
                resourceInfo = await KubernetesHelper.ResourceExists("Deployment", "keda", @namespace, returnJsonOutput: true);
            }
            
            return resourceInfo;
        }
    }
}
