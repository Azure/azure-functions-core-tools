using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    public interface IHostingPlatform
    {
        Task DeployContainerizedFunction(string functionName, string image, int min, int max);
        Task DeployContainerizedFunction(string functionName, string image, int min, int max, string resourceGroupName,
            string containerGroupName, string subscriptionId, string location, int port, string containerName, string imageName,
            double containerMemory, double containerCPU, string osType = "Linux");
    }
}