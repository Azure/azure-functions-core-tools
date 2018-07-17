using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    public interface IHostingPlatform
    {
        Task DeployContainerizedFunction(string functionName, string image, int min, int max);
    }
}