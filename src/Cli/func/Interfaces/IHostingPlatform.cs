using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    public interface IHostingPlatform
    {
        Task DeployContainerizedFunction(string functionName, string image, string nameSpace, int min, int max, double cpu, int memory, string port, string pullSecret);
    }
}