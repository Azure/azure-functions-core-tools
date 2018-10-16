using Azure.Functions.Cli.Helpers;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IAuthManager
    {
        Task CreateAADApplication(string accessToken, string AADName, WorkerRuntime workerRuntime, string appName = null);
    }
}