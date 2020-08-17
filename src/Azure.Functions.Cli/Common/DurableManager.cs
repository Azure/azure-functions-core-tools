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
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    internal class DurableManager : IDurableManager
    {
        private ISecretsManager _secretsManager;

        private AzureStorageOrchestrationService _orchestrationService;

        private TaskHubClient _client;

        private string _taskHubName;

        private string _connectionStringKey;

        public const string DefaultConnectionStringKey = "AzureWebJobsStorage";

        public const string DefaultTaskHubName = "DurableFunctionsHub";

        public const string DurableAzureStorageExtensionName = "DurableTask.AzureStorage.dll";

        public const string MinimumDurableAzureStorageExtensionVersion = "1.4.0";

        public readonly static DateTime CreatedAfterDefault = DateTime.MinValue;
        public readonly static DateTime CreatedBeforeDefault = DateTime.MaxValue.AddDays(-1); // subtract one to avoid overflow/timezone error


        public DurableManager(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
            SetConnectionStringAndTaskHubName();
        }

        private void SetConnectionStringAndTaskHubName()
        {
            // Set connection string key and task hub name to defaults
            _connectionStringKey = DefaultConnectionStringKey;
            _taskHubName = DefaultTaskHubName;

            try
            {
                if (File.Exists(ScriptConstants.HostMetadataFileName))
                {
                    // Attempt to retrieve Durable override settings from host.json
                    dynamic hostSettings = JObject.Parse(File.ReadAllText(ScriptConstants.HostMetadataFileName));
                    JObject durableTask = null;

                    string version = hostSettings["version"];
                    if (version?.Equals("2.0") == true)
                    {
                        // If the version is (explicitly) 2.0, prepend path to 'durableTask' with 'extensions'
                        durableTask = hostSettings?.extensions?.durableTask;
                    }
                    else
                    {
                        durableTask = hostSettings?.durableTask;
                    }

                    if (durableTask != null)
                    {
                        // Override connection string or task hub name if they exist in host.json
                        _connectionStringKey = durableTask.GetValue("AzureStorageConnectionStringName", StringComparison.OrdinalIgnoreCase)?.ToString()
                            ?? _connectionStringKey;
                        _taskHubName = durableTask.GetValue("HubName", StringComparison.OrdinalIgnoreCase)?.ToString()
                            ?? _taskHubName;
                    }
                }
                else
                {
                    ColoredConsole.WriteLine(WarningColor($"Could not find local host metadata file '{ScriptConstants.HostMetadataFileName}'"));
                }
            }
            catch (Exception e)
            {
                ColoredConsole.WriteLine(WarningColor($"Exception thrown while attempting to parse override connection string and task hub name from '{ScriptConstants.HostMetadataFileName}':"));
                ColoredConsole.WriteLine(WarningColor(e.Message));
            }
        }

        private void SetStorageServiceAndTaskHubClient(out AzureStorageOrchestrationService orchestrationService, out TaskHubClient taskHubClient, string connectionStringKey = null, string taskHubName = null)
        {
            _connectionStringKey = connectionStringKey ?? _connectionStringKey;
            _taskHubName = taskHubName ?? _taskHubName;

            var connectionString = Environment.GetEnvironmentVariable(_connectionStringKey); // Prioritize environment variables
            connectionString = connectionString ?? _secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(_connectionStringKey, StringComparison.OrdinalIgnoreCase)).Value;

            if (!string.IsNullOrEmpty(connectionString))
            {
                var settings = new AzureStorageOrchestrationServiceSettings
                {
                    TaskHubName = _taskHubName,
                    StorageConnectionString = connectionString,
                };

                orchestrationService = new AzureStorageOrchestrationService(settings);
                taskHubClient = new TaskHubClient(orchestrationService);
            }
            else
            {
                throw new CliException("No storage connection string found.");
            }
        }

        private void Initialize(out AzureStorageOrchestrationService orchestrationService, out TaskHubClient taskHubClient, string connectionStringKey = null, string taskHubName = null)
        {
            CheckAssemblies();
            SetStorageServiceAndTaskHubClient(out orchestrationService, out taskHubClient, connectionStringKey, taskHubName);
        }

        private void CheckAssemblies()
        {
            // Retrieve list of DurableTask.AzureStorage DLL files
            var assemblyFilePaths = Directory.GetFiles(Environment.CurrentDirectory, DurableAzureStorageExtensionName, SearchOption.AllDirectories);

            if (assemblyFilePaths.Count() == 0)
            {
                ColoredConsole.WriteLine(WarningColor($"Could not find {DurableAzureStorageExtensionName}. The functions host must be running a" +
                    $" Durable Functions app in order for Durable Functions CLI commands to work."));
                return;
            }

            // Sort list of DLL files by the last modified time of their enclosing bin folder. Choose most recent
            var mostRecentAssembly = assemblyFilePaths.Select(assemblyFilePath => new FileInfo(assemblyFilePath)).OrderBy(file => file.Directory.LastWriteTime).Last();

            var minimumExtensionVersion = new Version(MinimumDurableAzureStorageExtensionVersion);

            var mostRecentAssemblyVersion = new Version(FileVersionInfo.GetVersionInfo(mostRecentAssembly.FullName).FileVersion);

            if (mostRecentAssemblyVersion < minimumExtensionVersion)
            {
                throw new CliException($"Durable Functions CLI commands must be used with {DurableAzureStorageExtensionName} versions greater than or equal to {MinimumDurableAzureStorageExtensionVersion}" +
                    $"{Environment.NewLine}Your version: '{mostRecentAssemblyVersion}'. Path of DLL in question: {mostRecentAssembly.FullName}");
            }
        }

        public async Task DeleteTaskHub(string connectionStringKey, string taskHubName)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

            await _orchestrationService.DeleteAsync();

            ColoredConsole.Write(VerboseColor($"Task hub '{_taskHubName}' successfully deleted."));
        }

        public async Task GetHistory(string connectionStringKey, string taskHubName, string instanceId)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

            var historyString = await _orchestrationService.GetOrchestrationHistoryAsync(instanceId, null);

            JArray history = JArray.Parse(historyString);

            JArray chronologicalHistory = new JArray(history.OrderBy(obj => (string)obj["TimeStamp"]));

            foreach (JObject jobj in chronologicalHistory)
            {
                // Convert EventType enum values to their equivalent string value
                Enum.TryParse(jobj["EventType"].ToString(), out EventType eventName);
                jobj["EventType"] = eventName.ToString();
            }

            ColoredConsole.Write($"{chronologicalHistory.ToString(Formatting.Indented)}");
        }

        public async Task GetInstances(string connectionStringKey, string taskHubName, DateTime createdTimeFrom, DateTime createdTimeTo, IEnumerable<OrchestrationStatus> statuses, int top, string continuationToken)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

            DurableStatusQueryResult queryResult = await _orchestrationService.GetOrchestrationStateAsync(createdTimeFrom, createdTimeTo, statuses, top, continuationToken);

            // TODO? Status of each instance prints as an integer, rather than the string of the OrchestrationStatus enum
            ColoredConsole.WriteLine(JsonConvert.SerializeObject(queryResult.OrchestrationState, Formatting.Indented));

            ColoredConsole.WriteLine(VerboseColor($"Continuation token for next set of results: '{queryResult.ContinuationToken}'"));
        }

        public async Task GetRuntimeStatus(string connectionStringKey, string taskHubName, string instanceId, bool showInput, bool showOutput)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

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
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

            var runtimeStatusesArray = runtimeStatuses?.ToArray();
            var stats = await _orchestrationService.PurgeInstanceHistoryAsync(createdAfter, createdBefore, runtimeStatusesArray);

            string messageToPrint = $"Purged orchestration history for all instances created between '{createdAfter}' and '{createdBefore}'";

            if (runtimeStatusesArray != null)
            {
                string statuses = string.Join(",", runtimeStatusesArray);
                messageToPrint += $" and whose runtime status matched one of the following: [{statuses}]";
            }

            ColoredConsole.WriteLine(VerboseColor(messageToPrint));
            ColoredConsole.WriteLine($"Instances deleted: {stats.InstancesDeleted}");
            ColoredConsole.WriteLine($"Rows deleted: {stats.RowsDeleted}");
        }

        public async Task RaiseEvent(string connectionStringKey, string taskHubName, string instanceId, string eventName, object data)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };

            await _client.RaiseEventAsync(orchestrationInstance, eventName, data);

            ColoredConsole.WriteLine(VerboseColor($"Raised event '{eventName}' to instance '{instanceId}'."));
        }

        public async Task Rewind(string connectionStringKey, string taskHubName, string instanceId, string reason)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

            var oldStatus = await _client.GetOrchestrationStateAsync(instanceId, false);

            try
            {
                await _orchestrationService.RewindTaskOrchestrationAsync(instanceId, reason);
            }
            catch (ArgumentOutOfRangeException)
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
                ColoredConsole.Write(VerboseColor("Status before rewind: "));
                ColoredConsole.WriteLine($"{oldStatus[0].OrchestrationStatus}");
                ColoredConsole.Write(VerboseColor("Status after rewind: "));
                ColoredConsole.WriteLine($"{newStatus[0].OrchestrationStatus}");
            }
        }

        public async Task StartNew(string connectionStringKey, string taskHubName, string functionName, string instanceId, object data)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

            await _client.CreateOrchestrationInstanceAsync(functionName, version: string.Empty, instanceId: instanceId, input: data);

            var status = await _client.GetOrchestrationStateAsync(instanceId, false);
            if (status != null && status.Count > 0)
            {
                ColoredConsole.WriteLine(VerboseColor($"Started '{status[0].Name}' at {status[0].CreatedTime}. " +
                    $"Instance ID: '{status[0].OrchestrationInstance.InstanceId}'."));
            }
            else
            {
                throw new CliException($"Could not start new instance '{instanceId}'.");
            }
        }

        public async Task Terminate(string connectionStringKey, string taskHubName, string instanceId, string reason)
        {
            Initialize(out _orchestrationService, out _client, connectionStringKey, taskHubName);

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
                    ColoredConsole.WriteLine(VerboseColor($"Successfully terminated '{instanceId}'"));
                }
                else
                {
                    throw new CliException($"Instance did not terminate within the given timeout.");
                }
            }
            else
            {
                ColoredConsole.WriteLine(WarningColor($"Failed to find instance '{instanceId}'. No instance was terminated."));
            }
        }

        public static object RetrieveCommandInputData(string input)
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