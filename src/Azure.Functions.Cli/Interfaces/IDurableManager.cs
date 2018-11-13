using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DurableTask.Core;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IDurableManager
    {
        Task DeleteTaskHub(string connectionString);

        Task GetHistory(string connectionString, string instanceId);

        Task GetInstances(string connectionString, DateTime createdTimeFrom, DateTime createdTimeTo, IEnumerable<OrchestrationStatus> statuses, int top, string continuationToken);

        Task GetRuntimeStatus(string connectionString, string instanceId, bool showInput, bool showOutput);

        Task PurgeHistory(string connectionString, DateTime createdAfter, DateTime createdBefore, IEnumerable<OrchestrationStatus> runtimeStatuses);

        Task RaiseEvent(string connectionString, string instanceId, string eventName, object eventData);

        Task Rewind(string connectionString, string instanceId, string reason);

        Task StartNew(string connectionString, string functionName, string instanceId, object input);

        Task Terminate(string connectionString, string instanceId, string reason);
    }
}