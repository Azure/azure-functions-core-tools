// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using DurableTask.Core;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IDurableManager
    {
       internal Task DeleteTaskHub(string connectionString, string taskHubName);

       internal Task GetHistory(string connectionString, string taskHubName, string instanceId);

       internal Task GetInstances(string connectionString, string taskHubName, DateTime createdTimeFrom, DateTime createdTimeTo, IEnumerable<OrchestrationStatus> statuses, int top, string continuationToken);

       internal Task GetRuntimeStatus(string connectionString, string taskHubName, string instanceId, bool showInput, bool showOutput);

       internal Task PurgeHistory(string connectionString, string taskHubName, DateTime createdAfter, DateTime createdBefore, IEnumerable<OrchestrationStatus> runtimeStatuses);

       internal Task RaiseEvent(string connectionString, string taskHubName, string instanceId, string eventName, object eventData);

       internal Task Rewind(string connectionString, string taskHubName, string instanceId, string reason);

       internal Task StartNew(string connectionString, string taskHubName, string functionName, string instanceId, object input);

       internal Task Terminate(string connectionString, string taskHubName, string instanceId, string reason);
    }
}
