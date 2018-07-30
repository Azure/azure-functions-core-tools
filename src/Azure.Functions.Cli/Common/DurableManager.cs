using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using DurableTask.Core;
using DurableTask.AzureStorage;
using static Colors.Net.StringStaticMethods;
using DurableTask.Core.History;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Common
{
    internal class DurableManager
    {
        private readonly ISecretsManager _secretsManager;
        readonly AzureStorageOrchestrationService orchestrationService;
        readonly TaskHubClient client;

        public DurableManager(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;

            var connectionString = secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals("AzureWebJobsStorage", StringComparison.OrdinalIgnoreCase)).Value;

            var settings = new AzureStorageOrchestrationServiceSettings
            {
                TaskHubName = "DurableFunctionsHub",
                StorageConnectionString = connectionString,
            };

            this.orchestrationService = new AzureStorageOrchestrationService(settings);
            this.client = new TaskHubClient(orchestrationService);
        }

        public async Task StartNew(string functionName, string version, string instanceId, object input)
        {
            await client.CreateOrchestrationInstanceAsync(functionName, version, instanceId, input);
            var status = await client.GetOrchestrationStateAsync(instanceId, false);
            if (status != null)
            {
                ColoredConsole.WriteLine(Yellow($"Started {status[0].Name} with new instance {status[0].OrchestrationInstance} at {status[0].CreatedTime}"));
            }
            else
            {
                ColoredConsole.WriteLine(Red($"Could not start new instance {instanceId}"));
            }
        }

        public async Task Terminate(string instanceId, string reason)
        {
            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };
            await client.TerminateInstanceAsync(orchestrationInstance, reason);
            var status = await client.GetOrchestrationStateAsync(instanceId, false);
            ColoredConsole.WriteLine(Yellow($"Termination message sent to instance {instanceId}"));
        }

        public async Task Rewind(string instanceId, string reason)
        {
            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };
            await client.RewindInstanceAsync(orchestrationInstance, reason);
            var status = await client.GetOrchestrationStateAsync(instanceId, false);
            ColoredConsole.WriteLine(Yellow($"Rewind message sent to instance {instanceId}"));
        }


        public async Task RaiseEvent(string instanceId, string eventName, object eventData)
        {
            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };
            await client.RaiseEventAsync(orchestrationInstance, eventName, eventData);
            ColoredConsole.WriteLine(Yellow($"Rasied event {eventName} to instance {instanceId} with data {eventData}"));

        }

        public async Task GetHistory(string instanceId)
        {
            var historyString = await orchestrationService.GetOrchestrationHistoryAsync(instanceId, null);
            JArray history = JArray.Parse(historyString);
            JArray chronological_history = new JArray(history.OrderBy(obj => (string)obj["TimeStamp"]));

            foreach (JObject jobj in chronological_history)
            {
                var parsed = Enum.TryParse(jobj["EventType"].ToString(), out EventType eventName);
                jobj["EventType"] = eventName.ToString();
            }

            ColoredConsole.Write(Yellow($"History: {chronological_history.ToString(Formatting.Indented)}"));
        }

        public async Task GetOrchestrationState(string instanceId, bool allExecutions)
        {
            var statuses = await client.GetOrchestrationStateAsync(instanceId, allExecutions);
            foreach (OrchestrationState status in statuses)
            {
                ColoredConsole.WriteLine(Yellow($"Name: {status.Name}"))
                    .WriteLine(Yellow($"Instance: {status.OrchestrationInstance}"))
                    .WriteLine(Yellow($"Version: {status.Version}"))
                    .WriteLine(Yellow($"TimeCreated: {status.CreatedTime}"))
                    .WriteLine(Yellow($"CompletedTime: {status.CompletedTime}"))
                    .WriteLine(Yellow($"LastUpdatedTime: {status.LastUpdatedTime}"))
                    .WriteLine(Yellow($"Input: {status.Input}"))
                    .WriteLine(Yellow($"Output: {status.Output}"))
                    .WriteLine(Yellow($"Status: {status.OrchestrationStatus}"));
            }
        }

        public async Task DeleteHistory()
        {
            await orchestrationService.DeleteAsync();
            ColoredConsole.Write(Yellow("History and instance store deleted."));
        }
    }
}