using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Azure.Functions.Cli.Actions.DeployActions.Platforms.Models;
using Microsoft.Azure.Management.ContainerInstance;
using Microsoft.Azure.Management.ContainerInstance.Models;
using Microsoft.Rest;
using Microsoft.Azure.Services.AppAuthentication;
using System;
using Port = Microsoft.Azure.Management.ContainerInstance.Models.Port;
using Container = Microsoft.Azure.Management.ContainerInstance.Models.Container;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms
{
    public class ACIPlatform : IHostingPlatform
    {
        private string configFile = string.Empty;
        private const string FUNCTIONS_NAMESPACE = "azure-functions";
        private static ContainerInstanceManagementClient client;

        public ACIPlatform(string configFile)
        {

               
        }

        public static async Task<string> GetToken()
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var token = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.azure.com/");

            return token;
        }

        public async Task DeployContainerizedFunction(string functionName, string image, int min, int max)
        {
            await Deploy(functionName, image, FUNCTIONS_NAMESPACE, min, max);
        }

        private async Task Deploy(string functionName, string image, string fUNCTIONS_NAMESPACE, int min, int max)
        {
            ColoredConsole.WriteLine("Deploying function to ACI ...");
            var ResourceGroupName = "AzureFunctions-WestUS";
            var ContainerGroupName = "ContainerGroupName1";
            var token = await GetToken();
            client = new ContainerInstanceManagementClient(new TokenCredentials(token))
            {
                SubscriptionId = "SUBSCRIPTION-ID"
            };

            try
            {
                var containers = await client.ContainerGroups.ListByResourceGroupAsync(ResourceGroupName);
                var container = containers.Where(c => c.Name == ContainerGroupName).FirstOrDefault();


                if (container == null || container.IpAddress == null || container.IpAddress.Ip == null)
                {
                    ColoredConsole.WriteLine("Container not found ...");

                    var spec = new ContainerGroup
                    {
                        Location = "East US",
                        OsType = "Linux",
                        RestartPolicy = "Always",
                        IpAddress = new IpAddress
                        {
                            Ports = new[] { new Port(25565) }
                        },
                        Containers = new[]{
                        new Container
                        {
                            Name = "minecraft",
                            Image = "openhack/minecraft-server",
                            Ports = new []{ new ContainerPort(25565) },
                            EnvironmentVariables = new []{ new EnvironmentVariable("EULA","TRUE") },
                            Resources = new ResourceRequirements
                            {
                                Requests = new ResourceRequests(memoryInGB: 4, cpu: 2)
                            }
                        }
                        }
                    };
                    await client.ContainerGroups.CreateOrUpdateAsync(
                        resourceGroupName: ResourceGroupName,
                        containerGroupName: ContainerGroupName,
                        containerGroup: spec);

                    ColoredConsole.WriteLine("Container sucessefully created");
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.WriteLine(ex.Message.ToString());
                Console.ReadLine();
            }

        }
    }

    //    private async Task DeleteDeploymentIfExists(string name, string nameSpace)
    //    {
    //        //await client.DeploymentsV1Beta1().Delete(name, nameSpace);
    //    }

    //    private async Task Deploy(string name, string image, string nameSpace, int min, int max, double cpu = 0.1, int memory = 128, string port = "80")
    //    {
    //        //await CreateNamespace(nameSpace);
    //        //client.DefaultNamespace = nameSpace;

    //        //var deploymentName = $"{name}-deployment";

    //        //await DeleteDeploymentIfExists(deploymentName, nameSpace);

    //        //ColoredConsole.WriteLine("Deploying function to Kubernetes...");

    //        //var deployment = GetDeployment(deploymentName, image, cpu, memory, port, nameSpace, min);
    //        //var json = Newtonsoft.Json.JsonConvert.SerializeObject(deployment,
    //        //                Newtonsoft.Json.Formatting.None,
    //        //                new Newtonsoft.Json.JsonSerializerSettings
    //        //                {
    //        //                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
    //        //                });

    //        //File.WriteAllText("deployment.json", json);
    //        //KubernetesHelper.RunKubectl($"apply -f deployment.json");

    //        //ColoredConsole.WriteLine("Deployment successful");

    //        //var service = GetService(deploymentName, nameSpace, port);

    //        //try
    //        //{
    //        //    // we can safely ignore the error here
    //        //    await client.ServicesV1().Create(service);
    //        //}
    //        //catch { }

    //        //await TryRemoveAutoscaler(deploymentName, nameSpace);
    //        //await CreateAutoscaler(deploymentName, nameSpace, min, max);

    //        //var externalIP = "";

    //        //ColoredConsole.WriteLine("Waiting for External IP...");

    //        //while (string.IsNullOrEmpty(externalIP))
    //        //{
    //        //    var svc = await client.ServicesV1().Get($"{deploymentName}-service", nameSpace);
    //        //    if (svc != null)
    //        //    {
    //        //        if (svc.Status.LoadBalancer.Ingress.Count > 0)
    //        //        {
    //        //            externalIP = svc.Status.LoadBalancer.Ingress[0].Ip;
    //        //        }
    //        //    }
    //        //}

    //        //File.Delete("deployment.json");

    //        //ColoredConsole.WriteLine("");

    //        //ColoredConsole.WriteLine("Function deployed successfully!");
    //        //ColoredConsole.WriteLine($"Function IP: {externalIP}");
    //    }

    //    private async Task TryRemoveAutoscaler(string deploymentName, string nameSpace)
    //    {
    //        //await KubernetesHelper.RunKubectl($"delete hpa {deploymentName} -n {nameSpace}");
    //    }

    //    private async Task CreateAutoscaler(string deploymentName, string nameSpace, int minInstances, int maxInstances, int cpuPercentage = 60)
    //    {
    //        //var cmd = $"autoscale deploy {deploymentName} --cpu-percent={cpuPercentage} --max={maxInstances} --min={minInstances} --namespace={nameSpace}";

    //        //if (!string.IsNullOrEmpty(configFile))
    //        //{
    //        //    cmd += $" --kubeconfig {configFile}";
    //        //}

    //        //await KubernetesHelper.RunKubectl(cmd);
    //    }

    //    private ServiceV1 GetService(string name, string nameSpace, string port = "80")
    //    {
    //        //return new ServiceV1()
    //        //{
    //        //    Metadata = new ObjectMetaV1()
    //        //    {
    //        //        Name = $"{name}-service",
    //        //        Namespace = nameSpace
    //        //    },
    //        //    Spec = new ServiceSpecV1()
    //        //    {
    //        //        Selector = new Dictionary<string, string>()
    //        //        {
    //        //            {"app", name}
    //        //        },
    //        //        Ports = new List<ServicePortV1>()
    //        //        {
    //        //            new ServicePortV1()
    //        //            {
    //        //                Name = "http",
    //        //                Protocol = "TCP",
    //        //                Port = int.Parse(port)
    //        //            }
    //        //        },
    //        //        Type = "LoadBalancer"
    //        //    }
    //        //};
    //    }

    //    private Deployment GetDeployment(string name, string image, double cpu, int memory, string port, string nameSpace, int min)
    //    {
    //        //var deployment = new Deployment();
    //        //deployment.apiVersion = "apps/v1beta1";
    //        //deployment.kind = "Deployment";

    //        //var metadata = new Metadata();
    //        //metadata.@namespace = nameSpace;
    //        //metadata.name = name;
    //        //metadata.labels = new Labels();
    //        //metadata.labels.app = name;

    //        //deployment.metadata = metadata;
    //        //deployment.spec = new Spec();
    //        //deployment.spec.replicas = min;
    //        //deployment.spec.selector = new Selector();

    //        //deployment.spec.selector.matchLabels = new MatchLabels();
    //        //deployment.spec.selector.matchLabels.app = name;

    //        //deployment.spec.template = new Models.Template();
    //        //deployment.spec.template.metadata = new Metadata();
    //        //deployment.spec.template.metadata.labels = new Labels();
    //        //deployment.spec.template.metadata.labels.app = name;

    //        //deployment.spec.template.spec = new TemplateSpec();
    //        //deployment.spec.template.spec.containers = new List<Container>();
    //        //deployment.spec.template.spec.containers.Add(new Container()
    //        //{
    //        //    name = name,
    //        //    image = image,
    //        //    resources = new Resources()
    //        //    {
    //        //        requests = new Requests()
    //        //        {
    //        //            cpu = cpu.ToString(),
    //        //            memory = $"{memory}Mi"
    //        //        }
    //        //    }
    //        //});

    //        //if (!string.IsNullOrEmpty(port))
    //        //{
    //        //    deployment.spec.template.spec.containers[0].ports = new List<Port>()
    //        //    {
    //        //        new Port()
    //        //        {
    //        //            containerPort = int.Parse(port)
    //        //        }
    //        //    };
    //        //}

    //        //deployment.spec.template.spec.tolerations = new List<Toleration>()
    //        //{
    //        //    new Toleration()
    //        //    {
    //        //        key = "azure.com/aci",
    //        //        effect = "NoSchedule"
    //        //    }
    //        //};

    //        //return deployment;
    //    }

    //    private async Task CreateNamespace(string name)
    //    {
    //        await KubernetesHelper.RunKubectl($"create ns {name}");
    //    }
    //}
}