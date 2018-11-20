using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private ISecretsManager _secretsManager;

        private AzureStorageOrchestrationService _orchestrationService;

        private TaskHubClient _client;

        private string _taskHubName;

        private readonly string _connectionStringKey;

        public const string DefaultConnectionStringKey = "AzureWebJobsStorage";

        public const string DefaultTaskHubName = "DurableFunctionsHub";

        public const string DurableAzureStorageExtensionName = "DurableTask.AzureStorage.dll";

        public const string MinimumDurableAzureStorageExtensionVersion = "1.4.0";

        public DurableManager(ISecretsManager secretsManager)
        {
            this._secretsManager = secretsManager;
            this.SetConnectionStringAndTaskHubName(ref _taskHubName, ref _connectionStringKey);
        }

        private void SetConnectionStringAndTaskHubName(ref string taskHubName, ref string connectionStringKey)
        {
            // Set connection string key and task hub name to defaults
            connectionStringKey = DefaultConnectionStringKey;
            taskHubName = DefaultTaskHubName;

            try
            {
                if (File.Exists(ScriptConstants.HostMetadataFileName))
                {
                    // Attempt to retrieve Durable override settings from host.json
                    dynamic hostSettings = JObject.Parse(File.ReadAllText(ScriptConstants.HostMetadataFileName));

                    string version = hostSettings["version"];
                    if (version?.Equals("2.0") == true)
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
                else
                {
                    ColoredConsole.WriteLine(Yellow($"Could not find local host metadata file '{ScriptConstants.HostMetadataFileName}'"));
                }
            }
            catch (Exception e)
            {
                ColoredConsole.WriteLine(Yellow($"Exception thrown while attempting to parse override connection string and task hub name from '{ScriptConstants.HostMetadataFileName}':"));
                ColoredConsole.WriteLine(Yellow(e.Message));
            }     
        }

        private void SetStorageServiceAndTaskHubClient(ref AzureStorageOrchestrationService orchestrationService, ref TaskHubClient taskHubClient, string connectionStringKey = null, string taskHubName = null)
        {
            connectionStringKey = connectionStringKey ?? this._connectionStringKey;
            this._taskHubName = taskHubName ?? this._taskHubName;

            var connectionString = Environment.GetEnvironmentVariable(connectionStringKey); // Prioritize environment variables
            connectionString = connectionString ?? _secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(connectionStringKey, StringComparison.OrdinalIgnoreCase)).Value;

            if (!string.IsNullOrEmpty(connectionString))
            {
                var settings = new AzureStorageOrchestrationServiceSettings
                {
                    TaskHubName = _taskHubName,
                    StorageConnectionString = connectionString,
                };

                _orchestrationService = new AzureStorageOrchestrationService(settings);
                _client = new TaskHubClient(orchestrationService);
            }
            else
            {
                throw new CliException("No storage connection string found.");
            }
        }

        private void Initialize(ref AzureStorageOrchestrationService orchestrationService, ref TaskHubClient taskHubClient, string connectionStringKey = null, string taskHubName = null)
        {
            CheckAssemblies();
            SetStorageServiceAndTaskHubClient(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);
        }

        private void CheckAssemblies()
        {
            // Retrieve list of DurableTask.AzureStorage DLL files
            var assemblyFilePaths = Directory.GetFiles(Environment.CurrentDirectory, $"{DurableAzureStorageExtensionName}", SearchOption.AllDirectories).ToList<string>();
            
            if (assemblyFilePaths.Count() == 0)
            {
                ColoredConsole.WriteLine(Yellow($"Could not find {DurableAzureStorageExtensionName}. The functions host must be running a" +
                    $" Durable Functions app in order for Durable Functions CLI commands to work."));
                return;
            }

            // Convert them to a sorted list of FileInfo objects. Sort by the last modified time of the enclosing bin folder
            var assemblyFiles = assemblyFilePaths.Select(assemblyFilePath => new FileInfo(assemblyFilePath)).ToList().OrderBy(file => file.Directory.LastWriteTime);

            var minimumExtensionVersion = new Version(MinimumDurableAzureStorageExtensionVersion);

            var mostRecentAssembly = FileVersionInfo.GetVersionInfo(assemblyFiles.LastOrDefault().FullName);
            var mostRecentAssemblyVersion = new Version(mostRecentAssembly.FileVersion);

            if (mostRecentAssemblyVersion < minimumExtensionVersion)
            {
                 throw new CliException($"Durable Functions CLI commands must be used with {DurableAzureStorageExtensionName} versions greater than or equal to 1.4.0." +
                     $"{Environment.NewLine}Your version: '{mostRecentAssemblyVersion}'");
            }
        }

        public async Task DeleteTaskHub(string connectionStringKey, string taskHubName)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

            await _orchestrationService.DeleteAsync();

            ColoredConsole.Write(Green($"Task hub '{_taskHubName}' successfully deleted."));
        }

        public async Task GetHistory(string connectionStringKey, string taskHubName, string instanceId)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

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

        public async Task GetInstances(string connectionStringKey, string taskHubName, DateTime createdTimeFrom, DateTime createdTimeTo, IEnumerable<OrchestrationStatus> statuses, int top, string continuationToken)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

            DurableStatusQueryResult queryResult = await _orchestrationService.GetOrchestrationStateAsync(createdTimeFrom, createdTimeTo, statuses, top, continuationToken);

            // TODO? Status of each instance prints as an integer, rather than the string of the OrchestrationStatus enum
            ColoredConsole.WriteLine(JsonConvert.SerializeObject(queryResult.OrchestrationState, Formatting.Indented));

            ColoredConsole.WriteLine(Green($"Continuation token for next set of results: '{queryResult.ContinuationToken}'"));
        }

        public async Task GetRuntimeStatus(string connectionStringKey, string taskHubName, string instanceId, bool showInput, bool showOutput)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

            var status = (await _orchestrationService.GetOrchestrationStateAsync(instanceId, allExecutions: false, fetchInput: showInput)).FirstOrDefault();

            if (status == null)
            {
                ColoredConsole.WriteLine(Red($"Could not find an orchestration instance with id '{instanceId}'"));
            }
            else
            {
                status.Output = (showOutput) ? status.Output : null;
                ColoredConsole.WriteLine(JsonConvert.SerializeObject(status, Formatting.Indented));
            }            
        }

         public async Task PurgeHistory(string connectionStringKey, string taskHubName, DateTime createdAfter, DateTime createdBefore, IEnumerable<OrchestrationStatus> runtimeStatuses)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

            var stats = await _orchestrationService.PurgeInstanceHistoryAsync(createdAfter, createdBefore, runtimeStatuses);

            string messageToPrint = $"Purged orchestration history for all instances created between '{createdAfter}' and '{createdBefore}'";

            if (runtimeStatuses != null)
            {
                string statuses = string.Join(",", runtimeStatuses.Select(x => x.ToString()).ToArray());
                messageToPrint += $" and whose runtime status matched one of the following: [{statuses}]";
            }

            ColoredConsole.WriteLine(Green(messageToPrint));
            ColoredConsole.WriteLine($"Instances deleted: {stats.InstancesDeleted}");
            ColoredConsole.WriteLine($"Rows deleted: {stats.RowsDeleted}");
        }

        public async Task RaiseEvent(string connectionStringKey, string taskHubName, string instanceId, string eventName, object data)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.RaiseEventAsync(orchestrationInstance, eventName, data);

            ColoredConsole.WriteLine(Green($"Raised event '{eventName}' to instance '{instanceId}'."));
        }

        public async Task Rewind(string connectionStringKey, string taskHubName, string instanceId, string reason)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

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

        public async Task StartNew(string connectionStringKey, string taskHubName, string functionName, string instanceId, object data)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

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

        public async Task Terminate(string connectionStringKey, string taskHubName, string instanceId, string reason)
        {
            Initialize(ref _orchestrationService, ref _client, connectionStringKey, taskHubName);

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