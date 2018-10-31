using Azure.Functions.Cli.Helpers;
using System.Threading.Tasks;
namespace Azure.Functions.Cli.Interfaces
{
    internal interface IDurableManager
    {
        Task DeleteHistory();

        Task GetHistory(string instanceId);

        Task GetRuntimeStatus(string instanceId);

        Task RaiseEvent(string instanceId, string eventName, object eventData);

        Task Rewind(string instanceId, string reason);

        Task StartNew(string functionName, string version, string instanceId, object input);

        Task Terminate(string instanceId, string reason);                
    }
}