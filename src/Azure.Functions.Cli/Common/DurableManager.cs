using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using DurableTask.AzureStorage;
using DurableTask.Core;
using DurableTask.Core.History;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Common
{
    internal class DurableManager : IDurableManager
    {
        private readonly AzureStorageOrchestrationService _orchestrationService;

        private readonly TaskHubClient _client;

        private const string TaskHubName = "DurableFunctionsHub";

        public DurableManager(ISecretsManager secretsManager)
        {
            var connectionString = secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals("AzureWebJobsStorage", StringComparison.OrdinalIgnoreCase)).Value;
            if (connectionString == null)
            {
                throw new CliException("Unable to retrieve storage connection string.");
            }

            var settings = new AzureStorageOrchestrationServiceSettings
            {
                TaskHubName = TaskHubName,
                StorageConnectionString = connectionString,
            };

            _orchestrationService = new AzureStorageOrchestrationService(settings);
            _client = new TaskHubClient(_orchestrationService);
        }

        public async Task DeleteHistory()
        {
            await _orchestrationService.DeleteAsync();
            ColoredConsole.Write(Green("History and instance tables and queues successfully deleted."));
        }

        public async Task GetHistory(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to retrieve the history for.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to retrieve the history of." });
            }

            var historyString = await _orchestrationService.GetOrchestrationHistoryAsync(instanceId, null);

            JArray history = JArray.Parse(historyString);

            JArray chronological_history = new JArray(history.OrderBy(obj => (string)obj["TimeStamp"]));
            foreach (JObject jobj in chronological_history)
            {
                var parsed = Enum.TryParse(jobj["EventType"].ToString(), out EventType eventName);
                jobj["EventType"] = eventName.ToString();
            }

            ColoredConsole.Write($"History: {chronological_history.ToString(Formatting.Indented)}");
        }

        public async Task GetRuntimeStatus(string instanceId, bool getAllExecutions)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to get the runtime status of.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance for which to retrieve the runtime status." });
            }

            var statuses = await _client.GetOrchestrationStateAsync(instanceId, allExecutions: getAllExecutions);

            foreach (OrchestrationState status in statuses)
            {
                ColoredConsole.WriteLine($"Name: {status.Name}{Environment.NewLine}" +
                    $"Instance: {status.OrchestrationInstance}{Environment.NewLine}" +
                    $"Version: {status.Version}{Environment.NewLine}" +
                    $"TimeCreated: {status.CreatedTime}{Environment.NewLine}" +
                    $"CompletedTime: {status.CompletedTime}{Environment.NewLine}" +
                    $"LastUpdatedTime: {status.LastUpdatedTime}{Environment.NewLine}" +
                    $"Input: {status.Input}{Environment.NewLine}" +
                    $"Output: {status.Output}{Environment.NewLine}" +
                    $"Status: {status.OrchestrationStatus}{Environment.NewLine}");
            }
        }

        public async Task RaiseEvent(string instanceId, string eventName, object eventData)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to raise an event for.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to raise an event for." });
            }

            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.RaiseEventAsync(orchestrationInstance, eventName, eventData);

            ColoredConsole.WriteLine(Green($"Raised event {eventName} to instance {instanceId} with data {eventData}"));
        }

        public async Task Rewind(string instanceId, string reason)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to rewind.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to rewind." });
            }

            var oldStatus = await _client.GetOrchestrationStateAsync(instanceId, false);

            try
            {
                await _orchestrationService.RewindTaskOrchestrationAsync(instanceId, reason);
            }
            catch (ArgumentOutOfRangeException e)
            {
                // DurableTask.AzureStorage throws this error when it cannot find an orchestration instance matching the query
                throw new CliException("Orchestration instance not rewound. Must have a status of 'Failed', or an EventType of 'TaskFailed' or 'SubOrchestrationInstanceFailed' to be rewound.");
            }
            catch (Exception e)
            {
                throw new CliException(e.ToString());
            }
            
            ColoredConsole.WriteLine($"Rewind message sent to instance {instanceId}. Retrieving new status now..");

            // Wait three seconds before retrieving the updated status
            await Task.Delay(3000);
            var newStatus = await _client.GetOrchestrationStateAsync(instanceId, false);
            
            if (oldStatus != null && oldStatus.Count > 0 
                && newStatus != null && newStatus.Count > 0)
            {
                ColoredConsole.Write(Green("Status before rewind: "));
                ColoredConsole.Write($"{oldStatus[0].OrchestrationStatus}{Environment.NewLine}");
                ColoredConsole.Write(Green("Status after rewind: "));
                ColoredConsole.Write($"{newStatus[0].OrchestrationStatus}{Environment.NewLine}");
            }
        }

        public async Task StartNew(string functionName, string version, string instanceId, object input)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new CliArgumentsException("Must specify the name of of the orchestration function to start.",
                    new CliArgument { Name = "functionName", Description = "Name of the orchestration function to start." });
            }

            if (string.IsNullOrEmpty(instanceId))
            {
                instanceId = $"{Guid.NewGuid():N}";
            }

            await _client.CreateOrchestrationInstanceAsync(functionName, version, instanceId, input);

            var status = await _client.GetOrchestrationStateAsync(instanceId, false);

            if (status != null && status.Count > 0)
            {
                ColoredConsole.WriteLine(Green($"Started {status[0].Name} with new instance {status[0].OrchestrationInstance.InstanceId} at {status[0].CreatedTime}."));
            }
            else
            {
                throw new CliException($"Could not start new instance {instanceId}.");
            }
        }

        public async Task Terminate(string instanceId, string reason)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to terminate.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to terminate." });
            }

            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.TerminateInstanceAsync(orchestrationInstance, reason);

            var status = await _client.GetOrchestrationStateAsync(instanceId, false);

            if (status != null && status.Count > 0)
            {
                if (status[0].OrchestrationStatus == OrchestrationStatus.Running)
                {
                    ColoredConsole.WriteLine($"Found & terminated instance {instanceId}. Waiting 10 seconds for 'Terminated' status..");
                    // If it's still marked as running, wait a little bit and then poll again
                    await Task.Delay(10000);
                    status = await _client.GetOrchestrationStateAsync(instanceId, false);
                }

                if (status?[0]?.OrchestrationStatus == OrchestrationStatus.Terminated)
                {
                    ColoredConsole.WriteLine(Green($"Successfully terminated {instanceId}"));
                }
                else
                {
                    throw new CliException($"Instance did not terminate within 10 seconds. Current status: {status[0].OrchestrationStatus}");
                }
            }
            else
            {
                ColoredConsole.WriteLine(Yellow($"Failed to find instance {instanceId}. No instance was terminated."));
            }
        }       
    }
}