using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DurableTask.Core;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IDurableManager
    {
        Task DeleteTaskHub(string connectionString);

        Task GetHistory(string instanceId);

        Task GetInstances(DateTime createdTimeFrom, DateTime createdTimeTo, IEnumerable<OrchestrationStatus> statuses, int top, string continuationToken);

        Task GetRuntimeStatus(string instanceId, bool showInput, bool showOutput);

        Task PurgeHistory(DateTime createdAfter, DateTime createdBefore, IEnumerable<OrchestrationStatus> runtimeStatuses);

        Task RaiseEvent(string instanceId, string eventName, object eventData);

        Task Rewind(string instanceId, string reason);

        Task StartNew(string functionName, string instanceId, object input);

        Task Terminate(string instanceId, string reason);                
    }
}