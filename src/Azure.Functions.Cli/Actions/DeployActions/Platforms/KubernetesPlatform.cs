using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using KubeClient;
using KubeClient.Models;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms
{
    public class KubernetesPlatform : IHostingPlatform
    {
        private string configFile = string.Empty;
        private const string FUNCTIONS_NAMESPACE = "azure-functions";
        private static KubeApiClient client;

        public async Task DeployContainerizedFunction(string functionName, string image, int min, int max)
        {
            await Deploy(functionName, image, FUNCTIONS_NAMESPACE, min, max);
        }

        public KubernetesPlatform(string configFile)
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

        private async Task DeleteDeploymentIfExists(string name, string nameSpace)
        {
            await client.DeploymentsV1Beta1().Delete(name, nameSpace);
        }

        private async Task Deploy(string name, string image, string nameSpace, int min, int max, double cpu = 0.1, int memory = 128, string port = "80")
        {
            await CreateNamespace(nameSpace);
            client.DefaultNamespace = nameSpace;

            var deploymentName = $"{name}-deployment";

            await DeleteDeploymentIfExists(deploymentName, nameSpace);

            ColoredConsole.WriteLine("Deploying function to Kubernetes...");

            var deployment = GetDeployment(deploymentName, image, cpu, memory, port, nameSpace, min);
            await client.DeploymentsV1Beta1().Create(deployment);

            ColoredConsole.WriteLine("Deployment successful");

            var service = GetService(deploymentName, nameSpace, port);

            try
            {
                // we can safely ignore the error here
                await client.ServicesV1().Create(service);
            }
            catch {}

            await TryRemoveAutoscaler(deploymentName, nameSpace);
            await CreateAutoscaler(deploymentName, nameSpace, min, max);

            var externalIP = "";

            ColoredConsole.WriteLine("Waiting for External IP...");

            while (string.IsNullOrEmpty(externalIP))
            {
                var svc = await client.ServicesV1().Get($"{deploymentName}-service", nameSpace);
                if (svc != null)
                {
                    if (svc.Status.LoadBalancer.Ingress.Count > 0)
                    {
                        externalIP = svc.Status.LoadBalancer.Ingress[0].Ip;
                    }
                }
            }

            ColoredConsole.WriteLine("");

            ColoredConsole.WriteLine("Function deployed successfully!");
            ColoredConsole.WriteLine($"Function IP: {externalIP}");
        }

        private async Task TryRemoveAutoscaler(string deploymentName, string nameSpace)
        {
            await KubernetesHelper.RunKubectl($"delete hpa {deploymentName} -n {nameSpace}");
        }

        private async Task CreateAutoscaler(string deploymentName, string nameSpace, int minInstances, int maxInstances, int cpuPercentage = 60)
        {
            var cmd = $"autoscale deploy {deploymentName} --cpu-percent={cpuPercentage} --max={maxInstances} --min={minInstances} --namespace={nameSpace}";

            if (!string.IsNullOrEmpty(configFile))
            {
                cmd+= $" --kubeconfig {configFile}";
            }
            
            await KubernetesHelper.RunKubectl(cmd);
        }

        private ServiceV1 GetService(string name, string nameSpace, string port = "80")
        {
            return new ServiceV1()
            {
                Metadata = new ObjectMetaV1()
                {
                    Name = $"{name}-service",
                    Namespace = nameSpace
                },
                Spec = new ServiceSpecV1()
                {
                    Selector = new Dictionary<string, string>()
                    {
                        {"app", name}
                    },
                    Ports = new List<ServicePortV1>()
                    {
                        new ServicePortV1()
                        {
                            Name = "http",
                            Protocol = "TCP",
                            Port = int.Parse(port)
                        }
                    },
                    Type = "LoadBalancer"
                }
            };
        }

        private DeploymentV1Beta1 GetDeployment(string name, string image, double cpu, int memory, string port, string nameSpace, int min)
        {
            var deployment = new DeploymentV1Beta1();
            var metadata = new ObjectMetaV1();
            metadata.Namespace = nameSpace;

            var labels = new System.Collections.Generic.Dictionary<string, string>();
            labels.Add("app", name);

            metadata.Labels = labels;

            deployment.Metadata = metadata;
            deployment.Metadata.Name = name;

            deployment.Spec = new DeploymentSpecV1Beta1();
            deployment.Spec.Replicas = min;
            deployment.Spec.Selector = new LabelSelectorV1();
            deployment.Spec.Selector.MatchLabels = labels;
            deployment.Spec.ProgressDeadlineSeconds = 60;

            deployment.Spec.Template = new PodTemplateSpecV1();
            deployment.Spec.Template.Metadata = new ObjectMetaV1();
            deployment.Spec.Template.Metadata.Labels = labels;

            deployment.Spec.Template.Spec = new PodSpecV1();
            deployment.Spec.Template.Spec.Containers = new List<ContainerV1>();

            deployment.Spec.Template.Spec.Containers.Add(new ContainerV1()
            {
                Name = name,
                Image = image,
                Resources = new ResourceRequirementsV1()
                {
                    Requests = new Dictionary<string, string>()
                    {
                        {"cpu", cpu.ToString()},
                        {"memory", $"{memory}Mi"}
                    }
                }
            });

            if (!string.IsNullOrEmpty(port))
            {
                deployment.Spec.Template.Spec.Containers[0].Ports = new List<ContainerPortV1>()
                {
                        new ContainerPortV1()
                        {
                            ContainerPort = int.Parse(port)
                        }
                };
            }

            return deployment;
        }

        private async Task CreateNamespace(string name)
        {
            await KubernetesHelper.RunKubectl($"create ns {name}");
        }
    }
}