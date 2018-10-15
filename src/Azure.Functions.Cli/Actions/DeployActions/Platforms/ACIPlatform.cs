using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
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
        private static ContainerInstanceManagementClient client;

        public ACIPlatform(string configFile)
        {
            throw new NotImplementedException("Use of configuration file is not supported by ACI at this time.");
        }
        public ACIPlatform()
        {
           
        }

        public static async Task<string> GetToken()
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            var token = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.azure.com/");

            return token;
        }

        public async Task DeployContainerizedFunction(string functionName, string image, int min, int max, string resourceGroupName, string containerGroupName, string subscriptionId, string location, int port, double containerMemory, double containerCPU, string osType = "Linux")
        {
            await Deploy(functionName,image,min,max,resourceGroupName,containerGroupName,subscriptionId,location,port,containerMemory,containerCPU ,osType);
        }

        private async Task Deploy(string functionName, string image, int min, int max, string resourceGroupName, string containerGroupName, string subscriptionId, string location, int port, double containerMemory, double containerCPU, string osType = "Linux")
        {
            ColoredConsole.WriteLine("Deploying function to ACI ...");
            var ResourceGroupName = resourceGroupName;
            var ContainerGroupName = containerGroupName;
            string token = await GetToken();
            
            client = new ContainerInstanceManagementClient(new TokenCredentials(token))
            {
                SubscriptionId = subscriptionId
            };

            try
            {
                var containerGroups = await client.ContainerGroups.ListByResourceGroupAsync(ResourceGroupName);
                ContainerGroup containerGroup = containerGroups.Where(c => c.Name == ContainerGroupName).FirstOrDefault();

                if (containerGroup == null || containerGroup.IpAddress.Ip == null || containerGroup.IpAddress == null)
                {
                    ColoredConsole.WriteLine("Container group does not exist ...");

                    ContainerGroup containerDefinition = new ContainerGroup
                    {
                        Location = location,
                        OsType = osType,
                        RestartPolicy = "Always",
                        IpAddress = new IpAddress
                        {
                            Ports = new[] { new Port(port)}
                        },
                        Containers = new[]{
                        new Container
                        {
                            Name = functionName,
                            Image = image,
                            Ports = new []{ new ContainerPort(port) },
                            Resources = new ResourceRequirements
                            {
                                Requests = new ResourceRequests(memoryInGB: containerMemory, cpu: containerCPU)
                            }
                        }
                        }
                    };
                    await client.ContainerGroups.CreateOrUpdateAsync(
                        resourceGroupName: ResourceGroupName,
                        containerGroupName: ContainerGroupName,
                        containerGroup: containerDefinition);

                    ColoredConsole.WriteLine("Container group sucessefully created.");                    
                }
                else
                {
                    ColoredConsole.WriteLine("Container group already exist!");
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.WriteLine(ex.Message.ToString());
            } 
        }
        public Task DeployContainerizedFunction(string functionName, string image, int min, int max)
        {
            throw new NotImplementedException("This operation is not supported by ACI. Try --platform kubernetes");
        }
    }    
}