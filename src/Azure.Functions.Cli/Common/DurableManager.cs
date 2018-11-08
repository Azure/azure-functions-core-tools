using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using DurableTask.AzureStorage;
using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Common
{
    internal class DurableManager : IDurableManager
    {
        private readonly AzureStorageOrchestrationService _orchestrationService;

        private readonly TaskHubClient _client;

        private readonly string _taskHubName;

        private readonly string _connectionString;

        public const string DefaultConnectionStringKey = "AzureWebJobsStorage";

        public const string DefaultTaskHubName = "DurableFunctionsHub";

        public DurableManager(ISecretsManager secretsManager)
        {
            this.SetConnectionStringAndTaskHubName(secretsManager, ref _taskHubName, ref _connectionString);

            if (!string.IsNullOrEmpty(_connectionString))
            {
                var settings = new AzureStorageOrchestrationServiceSettings
                {
                    TaskHubName = _taskHubName,
                    StorageConnectionString = _connectionString,
                };

                _orchestrationService = new AzureStorageOrchestrationService(settings);
                _client = new TaskHubClient(_orchestrationService);
            }
        }

        // HELP WANTED: Is there a better way to do this?? (parse host.json for durable settings)
        private void SetConnectionStringAndTaskHubName(ISecretsManager secretsManager, ref string taskHubName, ref string connectionString)
        {       
            // Set connection string key and task hub name to defaults
            var connectionStringKey = DefaultConnectionStringKey;
            taskHubName = DefaultTaskHubName;

            try
            {
                // Attempt to retrieve Durable override settings from host.json
                dynamic hostSettings = JObject.Parse(File.ReadAllText(ScriptConstants.HostMetadataFileName));

                if (hostSettings.version?.Equals("2.0") == true)
                {
                    // If the version is (explicitly) 2.0, prepend path to 'durableTask' with 'extensions'
                    connectionStringKey = hostSettings?.extensions?.durableTask?.AzureStorageConnectionStringName ?? connectionStringKey;
                    taskHubName = hostSettings?.extensions?.durableTask?.HubName ?? taskHubName;
                }
                else
                {
                    connectionStringKey = hostSettings?.durableTask?.AzureStorageConnectionStringName ?? connectionStringKey;
                    taskHubName = hostSettings?.durableTask?.HubName ?? taskHubName;
                }
            }
            catch(Exception e)
            {
                ColoredConsole.WriteLine(Yellow($"Exception thrown while attempting to parse override connection string and task hub name from {ScriptConstants.HostMetadataFileName}:"));
                ColoredConsole.WriteLine(Yellow(e.Message));
            }

            connectionString = secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(connectionStringKey, StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrEmpty(connectionString))
            {
                // Warn user rather than throwing an error in the case of 1. manual override with --connection-string or 2. testing
                ColoredConsole.WriteLine(Yellow($"Unable to retrieve storage connection string with key '{connectionStringKey}'"));
            }
        }


        public async Task DeleteTaskHub(string connectionString)
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                var settings = new AzureStorageOrchestrationServiceSettings
                {
                    TaskHubName = _taskHubName,
                    StorageConnectionString = connectionString,
                };

                var orchestrationService = new AzureStorageOrchestrationService(settings);
                await orchestrationService.DeleteAsync();
            }
            else
            {
                await _orchestrationService.DeleteAsync();
            }

            ColoredConsole.Write(Green("Task hub successfully deleted."));
        }

        public async Task GetHistory(string instanceId)
        {
            var historyString = await _orchestrationService.GetOrchestrationHistoryAsync(instanceId, null);

            JArray history = JArray.Parse(historyString);

            JArray chronological_history = new JArray(history.OrderBy(obj => (string)obj["TimeStamp"]));
            foreach (JObject jobj in chronological_history)
            {
                // Convert EventType enum values to their equivalent string value
                var parsed = Enum.TryParse(jobj["EventType"].ToString(), out EventType eventName);
                jobj["EventType"] = eventName.ToString();
            }

            ColoredConsole.Write($"{chronological_history.ToString(Formatting.Indented)}");
        }

        public async Task GetInstances(DateTime createdTimeFrom, DateTime createdTimeTo, IEnumerable<OrchestrationStatus> statuses, int top, string continuationToken)
        {
            DurableStatusQueryResult queryResult = await _orchestrationService.GetOrchestrationStateAsync(createdTimeFrom, createdTimeTo, statuses, top, continuationToken);           

            // TODO? Status of each instance prints as an integer, rather than the string of the OrchestrationStatus enum
            ColoredConsole.WriteLine(JsonConvert.SerializeObject(queryResult.OrchestrationState, Formatting.Indented));

            ColoredConsole.WriteLine(Green($"Continuation token for next set of results: '{queryResult.ContinuationToken}'"));
        }

        public async Task GetRuntimeStatus(string instanceId, bool showInput, bool showOutput)
        {
            var statuses = await _orchestrationService.GetOrchestrationStateAsync(instanceId, allExecutions: false, fetchInput: showInput);

            foreach (OrchestrationState status in statuses)
            {
                status.Output = (showOutput) ? status.Output : null;
                ColoredConsole.WriteLine(JsonConvert.SerializeObject(status, Formatting.Indented));
            }
        }

        public async Task PurgeHistory(DateTime createdAfter, DateTime createdBefore, IEnumerable<OrchestrationStatus> runtimeStatuses)
        {
            await _orchestrationService.PurgeInstanceHistoryAsync(createdAfter, createdBefore, runtimeStatuses);

            string messageToPrint = $"Purged orchestration history of instances created between '{createdAfter}' and '{createdBefore}'";

            if (runtimeStatuses != null)
            {
                string statuses = string.Join(",", runtimeStatuses.Select(x => x.ToString()).ToArray());
                messageToPrint += $" and whose runtime status matched one of the following: [{statuses}]";
            }

            ColoredConsole.WriteLine(Green(messageToPrint));
        }

        public async Task RaiseEvent(string instanceId, string eventName, object data)
        {
            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.RaiseEventAsync(orchestrationInstance, eventName, data);

            ColoredConsole.WriteLine(Green($"Raised event '{eventName}' to instance '{instanceId}'."));
        }

        public async Task Rewind(string instanceId, string reason)
        {
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
            
            ColoredConsole.WriteLine($"Rewind message sent to instance '{instanceId}'. Retrieving new status now...");

            // Wait three seconds before retrieving the updated status
            await Task.Delay(3000);
            var newStatus = await _client.GetOrchestrationStateAsync(instanceId, false);
            
            if (oldStatus != null && oldStatus.Count > 0 
                && newStatus != null && newStatus.Count > 0)
            {
                ColoredConsole.Write(Green("Status before rewind: "));
                ColoredConsole.WriteLine($"{oldStatus[0].OrchestrationStatus}");
                ColoredConsole.Write(Green("Status after rewind: "));
                ColoredConsole.WriteLine($"{newStatus[0].OrchestrationStatus}");
            }
        }

        public async Task StartNew(string functionName, string instanceId, object data)
        {           
            if (string.IsNullOrEmpty(instanceId))
            {
                instanceId = $"{Guid.NewGuid():N}";
            }

            await _client.CreateOrchestrationInstanceAsync(functionName, version: string.Empty, instanceId: instanceId, input: data);

            var status = await _client.GetOrchestrationStateAsync(instanceId, false);

            if (status != null && status.Count > 0)
            {
                ColoredConsole.WriteLine(Green($"Started '{status[0].Name}' at {status[0].CreatedTime}. " +
                    $"Instance ID: '{status[0].OrchestrationInstance.InstanceId}'."));
            }
            else
            {
                throw new CliException($"Could not start new instance '{instanceId}'.");
            }
        }

        public async Task Terminate(string instanceId, string reason)
        {
            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.TerminateInstanceAsync(orchestrationInstance, reason);

            var status = (await _client.GetOrchestrationStateAsync(instanceId, false)).FirstOrDefault();

            if (status != null)
            {
                if (status.OrchestrationStatus != OrchestrationStatus.Terminated)
                {
                    ColoredConsole.WriteLine($"Sent a termination message to instance '{instanceId}'. Waiting up to 30 seconds for the orchestration to actually terminate...");
                    status = await _client.WaitForOrchestrationAsync(
                        orchestrationInstance,
                        timeout: TimeSpan.FromSeconds(30),
                        cancellationToken: CancellationToken.None);
                }

                if (status?.OrchestrationStatus == OrchestrationStatus.Terminated)
                {
                    ColoredConsole.WriteLine(Green($"Successfully terminated '{instanceId}'"));
                }
                else
                {
                    throw new CliException($"Instance did not terminate within the given timeout.");
                }
            }
            else
            {
                ColoredConsole.WriteLine(Yellow($"Failed to find instance '{instanceId}'. No instance was terminated."));
            }
        }

        public static dynamic DeserializeInstanceInput(string input)
        {
            // User passed a filename. Retrieve the contents
            if (!string.IsNullOrEmpty(input) && input[0] == '@')
            {
                string contents = string.Empty;
                string filePath = input.Substring(1);
                if (File.Exists(filePath))
                {
                    contents = File.ReadAllText(filePath);
                }
                else
                {
                    throw new CliException($"Could not find input file at '{filePath}'");
                }

                try
                {
                    return JsonConvert.DeserializeObject(contents);
                }
                catch (Exception e)
                {
                    throw new CliException($"Could not deserialize the input to the orchestration instance into a valid JSON object.", e);
                }
            }

            return input;
        }

        public static IEnumerable<OrchestrationStatus> ParseStatuses(string statusString)
        {
            // Convert the string list of statuses to filter by into an array of enums
            IEnumerable<OrchestrationStatus> statuses = null;
            if (!string.IsNullOrEmpty(statusString))
            {
                string[] statusArray = statusString.Split(' ');
                statuses = statusArray.Select(s => (OrchestrationStatus)Enum.Parse(typeof(OrchestrationStatus), s, ignoreCase: true));
            }

            return statuses;
        }
    }
}