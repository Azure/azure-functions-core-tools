using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Azure.Functions.Cli.Kubernetes;
using Azure.Functions.Cli.Kubernetes.Models.Knative;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms
{
    public class KnativePlatform : IHostingPlatform
    {
        private string configFile = string.Empty;
        private const string FUNCTIONS_NAMESPACE = "azure-functions";

        public async Task DeployContainerizedFunction(string functionName, string image, string nameSpace, int min, int max, double cpu = 0.1, int memory = 128, string port = "80", string pullSecret = "")
        {
            await Deploy(functionName, image, nameSpace, min, max);
        }

        private async Task Deploy(string name, string image, string nameSpace, int min, int max)
        {
            var isHTTP = IsHTTPTrigger(name);

            await CreateNamespace(nameSpace);

            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("Deploying function to Knative...");

            var knativeService = GetKnativeService(name, image, nameSpace, min, max, isHTTP);
            await KubectlHelper.KubectlApply(knativeService, true);

            var endpoint = await GetIstioClusterIngressEndpoint();
            var host = GetFunctionHost(name, nameSpace);

            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("Function deployed successfully!");
            ColoredConsole.WriteLine();
            if (string.IsNullOrEmpty(endpoint))
            {
                ColoredConsole.WriteLine($"Function URL: http://{endpoint}");
            }
            else
            {
                ColoredConsole.WriteLine("Couldn't identify Function URL: Couldn't find Istio Cluster Ingress endpoint");
            }
            ColoredConsole.WriteLine($"Function Host: {host}");
            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("Plese note: it may take a few minutes for the knative service to be reachable");
        }

        private string GetFunctionHost(string functionName, string nameSpace)
        {
            return string.Format("{0}.{1}.example.com", functionName, nameSpace);
        }

        private bool IsHTTPTrigger(string functionName)
        {
            var str = File.ReadAllText(string.Format("{0}/function.json", functionName));
            var jObj = JsonConvert.DeserializeObject<JObject>(str);
            return jObj["bindings"].Any(d => d["type"]?.ToString() == "httpTrigger");
        }

        private KnativeService GetKnativeService(string name, string image, string nameSpace, int min, int max, bool isHTTP)
        {
            var knativeService = new KnativeService();
            knativeService.kind = "Service";
            knativeService.apiVersion = "serving.knative.dev/v1alpha1";
            knativeService.metadata = new Metadata();
            knativeService.metadata.name = name;
            knativeService.metadata.@namespace = nameSpace;

            knativeService.spec = new KnativeSpec();
            knativeService.spec.runLatest = new RunLatest();
            knativeService.spec.runLatest.configuration = new Configuration();
            knativeService.spec.runLatest.configuration.revisionTemplate = new RevisionTemplate();
            knativeService.spec.runLatest.configuration.revisionTemplate.spec = new RevisionTemplateSpec();
            knativeService.spec.runLatest.configuration.revisionTemplate.spec.container = new KnativeContainer();
            knativeService.spec.runLatest.configuration.revisionTemplate.spec.container.image = image;

            knativeService.spec.runLatest.configuration.revisionTemplate.metadata = new RevisionTemplateMetadata();
            knativeService.spec.runLatest.configuration.revisionTemplate.metadata.annotations = new Dictionary<string, string>();

            // opt out of knative scale-to-zero for non-http triggers
            if (!isHTTP)
            {
                knativeService.spec.runLatest.configuration.revisionTemplate.metadata.annotations.Add("autoscaling.knative.dev/minScale", min.ToString());
            }

            if (max > 0)
            {
                knativeService.spec.runLatest.configuration.revisionTemplate.metadata.annotations.Add("autoscaling.knative.dev/maxScale", max.ToString());
            }

            return knativeService;
        }

        private async Task<string> GetIstioClusterIngressEndpoint()
        {
            var gateway = await KubectlHelper.KubectlGet<JObject>("service istio-ingressgateway --namespace istio-system");
            if (gateway == null)
            {
                return string.Empty;
            }

            var endpoint = gateway.SelectToken("Status.LoadBalancer.Ingress[0].Hostname")?.ToString();
            if (!string.IsNullOrEmpty(endpoint))
            {
                return endpoint;
            }

            return gateway.SelectToken("Status.LoadBalancer.Ingress[0].Ip")?.ToString();
        }

        private async Task CreateNamespace(string name)
        {
            if (!await KubernetesHelper.NamespaceExists(name))
            {
                await KubernetesHelper.CreateNamespace(name);
            }
        }
    }
}

