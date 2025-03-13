using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
using Azure.Functions.Cli.Kubernetes;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms
{
    public class KubernetesPlatform : IHostingPlatform
    {
        private string configFile = string.Empty;
        private const string FUNCTIONS_NAMESPACE = "azure-functions";

        public async Task DeployContainerizedFunction(string functionName, string image, string nameSpace, int min, int max, double cpu = 0.1, int memory = 128, string port = "80", string pullSecret = "")
        {
            await Deploy(functionName, image, nameSpace, min, max, cpu, memory, port, pullSecret);
        }

        private async Task DeleteDeploymentIfExists(string name, string nameSpace)
        {
            await KubectlHelper.RunKubectl($"delete deployment {name} --namespace {nameSpace}");
        }

        private async Task Deploy(string name, string image, string nameSpace, int min, int max, double cpu, int memory, string port, string pullSecret)
        {
            await CreateNamespace(nameSpace);

            var deploymentName = $"{name}-deployment";

            await DeleteDeploymentIfExists(deploymentName, nameSpace);

            ColoredConsole.WriteLine("Deploying function to Kubernetes...");

            var deployment = GetDeployment(deploymentName, image, cpu, memory, port, nameSpace, min, pullSecret);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(deployment,
                            Newtonsoft.Json.Formatting.None,
                            new Newtonsoft.Json.JsonSerializerSettings
                            {
                                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
                            });

            await KubectlHelper.RunKubectl($"apply -f deployment.json --namespace {nameSpace}");

            ColoredConsole.WriteLine("Deployment successful");

            var service = GetService(deploymentName, nameSpace, port);

            try
            {
                // we can safely ignore the error here
                await KubectlHelper.KubectlApply(service, showOutput: false);
            }
            catch { }

            await TryRemoveAutoscaler(deploymentName, nameSpace);
            await CreateAutoscaler(deploymentName, nameSpace, min, max);

            var externalIP = "";

            ColoredConsole.WriteLine("Waiting for External IP...");

            while (string.IsNullOrEmpty(externalIP))
            {
                var svc = await KubectlHelper.KubectlGet<JObject>($"{deploymentName}-service --namespace {nameSpace}");
                if (svc != null)
                {
                    var obj = svc.SelectToken("Status.LoadBalancer.Ingress[0].Ip");
                    if (obj != null)
                    {
                        externalIP = obj.ToString();
                    }
                }
            }

            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("Function deployed successfully!");
            ColoredConsole.WriteLine($"Function IP: {externalIP}");
        }

        private async Task TryRemoveAutoscaler(string deploymentName, string nameSpace)
        {
            await KubectlHelper.RunKubectl($"delete hpa {deploymentName} -n {nameSpace}");
        }

        private async Task CreateAutoscaler(string deploymentName, string nameSpace, int minInstances, int maxInstances, int cpuPercentage = 60)
        {
            var cmd = $"autoscale deploy {deploymentName} --cpu-percent={cpuPercentage} --max={maxInstances} --min={minInstances} --namespace={nameSpace}";

            if (!string.IsNullOrEmpty(configFile))
            {
                cmd += $" --kubeconfig {configFile}";
            }

            await KubectlHelper.RunKubectl(cmd);
        }

        private ServiceV1 GetService(string name, string nameSpace, string port = "80")
        {
            return new ServiceV1()
            {
                Metadata = new ObjectMetadataV1()
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

        private DeploymentV1Apps GetDeployment(string name, string image, double cpu, int memory, string port, string nameSpace, int min, string pullSecret)
        {
            var deployment = new DeploymentV1Apps
            {

                ApiVersion = "apps/v1beta1",
                Kind = "Deployment",

                Metadata = new ObjectMetadataV1
                {
                    Namespace = nameSpace,
                    Name = name,
                    Labels = new Dictionary<string, string>
                    {
                       { "app", name }
                    }
                },
                Spec = new DeploymentSpecV1Apps
                {
                    Replicas = min,
                    Selector = new SelectorV1
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            { "app", name }
                        }
                    },

                    Template = new PodTemplateV1
                    {
                        Metadata = new ObjectMetadataV1
                        {
                            Labels = new Dictionary<string, string>
                            {
                                { "app", name }
                            }
                        },
                        Spec = new PodTemplateSpecV1
                        {
                            Containers = new ContainerV1[]
                            {
                                new ContainerV1
                                {
                                    Name = name,
                                    Image = image,
                                    Resources = new ContainerResourcesV1()
                                    {
                                        Requests = new ContainerResourceRequestsV1()
                                        {
                                            Cpu = cpu.ToString(),
                                            Memory = $"{memory}Mi"
                                        }
                                    },
                                    Ports = string.IsNullOrEmpty(port)
                                        ? Array.Empty<ContainerPortV1>()
                                        : new ContainerPortV1[] { new ContainerPortV1 { ContainerPort = int.Parse(port) } },

                                }
                            },
                            Tolerations = new PodTolerationV1[]
                            {
                                new PodTolerationV1
                                {
                                    Key = "azure.com/aci",
                                    Effect = "NoSchedule"
                                }
                            },
                            ImagePullSecrets = string.IsNullOrEmpty(pullSecret)
                                ? null
                                : new ImagePullSecretV1[] { new ImagePullSecretV1 { Name = pullSecret } }
                        }
                    }
                }
            };

            return deployment;
        }

        private async Task CreateNamespace(string name)
        {
            await KubectlHelper.RunKubectl($"create ns {name}");
        }
    }
}