using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using KubeClient;
using KubeClient.Models;
using Azure.Functions.Cli.Actions.DeployActions.Platforms.Models;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms
{
    public class KnativePlatform : IHostingPlatform
    {
        private string configFile = string.Empty;
        private const string FUNCTIONS_NAMESPACE = "azure-functions";
        private static KubeApiClient client;

        public async Task DeployContainerizedFunction(string functionName, string image, string nameSpace, int min, int max, double cpu = 0.1, int memory = 128, string port = "80", string pullSecret = "")
        {
            await Deploy(functionName, image, FUNCTIONS_NAMESPACE, min, max);
        }

        public KnativePlatform(string configFile)
        {
            this.configFile = configFile;
            KubeClientOptions options;

            if (!string.IsNullOrEmpty(configFile))
            {
                options = K8sConfig.Load(configFile).ToKubeClientOptions(defaultKubeNamespace: FUNCTIONS_NAMESPACE);
            }
            else
            {
                options = K8sConfig.Load().ToKubeClientOptions(defaultKubeNamespace: FUNCTIONS_NAMESPACE);
            }

            client = KubeApiClient.Create(options);
        }

        private async Task Deploy(string name, string image, string nameSpace, int min, int max)
        {
            var isHTTP = IsHTTPTrigger(name);

            await CreateNamespace(nameSpace);
            client.DefaultNamespace = nameSpace;

            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("Deploying function to Knative...");

            var knativeService = GetKnativeService(name, image, nameSpace, min, max, isHTTP);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(knativeService,
                            Newtonsoft.Json.Formatting.None,
                            new Newtonsoft.Json.JsonSerializerSettings
                            {
                                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
                            });

            File.WriteAllText("deployment.json", json);
            await KubernetesHelper.RunKubectl($"apply -f deployment.json");
            File.Delete("deployment.json");

            var endpoint = await GetIstioClusterIngressEndpoint();
            if (string.IsNullOrEmpty(endpoint))
            {
                ColoredConsole.WriteLine("Couldn't find Istio Cluster Ingress External IP");
                return;
            }

            var host = GetFunctionHost(name, nameSpace);

            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("Function deployed successfully!");
            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine($"Function URL: http://{endpoint}");
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
            var jObj = JsonConvert.DeserializeObject<FunctionJson>(str);
            return jObj.bindings.Any(d=> d.type == "httpTrigger");
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
            var gateway = await client.ServicesV1().Get("istio-ingressgateway", "istio-system");
            if (gateway == null)
            {
                return "";
            }

            var endpoint = gateway.Status.LoadBalancer.Ingress[0].Hostname;
            if (!string.IsNullOrEmpty(endpoint))
            {
                return endpoint;
            }

            return gateway.Status.LoadBalancer.Ingress[0].Ip;
        }

        private async Task CreateNamespace(string name)
        {
            await KubernetesHelper.RunKubectl($"create ns {name}");
        }
    }
}

