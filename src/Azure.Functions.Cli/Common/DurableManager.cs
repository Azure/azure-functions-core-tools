using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using DurableTask.Core;
using DurableTask.AzureStorage;
using static Colors.Net.StringStaticMethods;
using static DurableTask.AzureStorage.AzureStorageOrchestrationService;
using System.Threading;
using DurableTask.Core.History;
using DurableTask.Core.Serializing;
using DurableTask.Core.Settings;
using System.Runtime.ExceptionServices;


namespace Azure.Functions.Cli.Common
{
    internal class DurableManager
    {
        private readonly ISecretsManager _secretsManager;
        //readonly AzureStorageOrchestrationService orchestrationService;
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

            var orchestrationService = new AzureStorageOrchestrationService(settings);
            this.client = new TaskHubClient(orchestrationService);
        }


        public Task Hello()
        {
            
            ColoredConsole.WriteLine(Yellow("THIS IS FOR DURABLE."));
            return Task.CompletedTask;
        }
        public async Task StartNew(string functionName, string version, string instanceId, object input)
        {
            await client.CreateOrchestrationInstanceAsync(functionName, version, instanceId, input);
            var status = await client.GetOrchestrationStateAsync(instanceId, false);
            if(status!= null)
            {
                ColoredConsole.WriteLine(Green($"Started {status[0].Name} with new instance {status[0].OrchestrationInstance} at {status[0].CreatedTime}"));
            }
            else
            {
                ColoredConsole.WriteLine(Red($"Could not start new instance {instanceId}"));
            }
        }

        public async Task Terminate(string instanceId, string reason)
        {
            var retryCount = 6;
            var orchestrationInstance = new OrchestrationInstance
            {
                InstanceId = instanceId
            };
            await client.TerminateInstanceAsync(orchestrationInstance, reason);
            var status = await client.GetOrchestrationStateAsync(instanceId, false);
            while (status[0].OrchestrationStatus.ToString() != "Terminated" && retryCount-- >= 0)
            {
                await Task.Delay(1000);
            }
            if (status[0].OrchestrationStatus.ToString() != "Terminated")
            {
                ColoredConsole.WriteLine(Red($"Could not terminate instance {status[0].OrchestrationStatus.ToString()}"));
            }
            else
            {
                ColoredConsole.WriteLine(Green($"Successfully terminated instance {instanceId}"));
            }
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

        public async Task GetOrchestrationState(string instanceId, bool allExecutions)
        {
            var statuses = await client.GetOrchestrationStateAsync(instanceId, allExecutions);
            foreach (OrchestrationState status in statuses) {
                ColoredConsole.WriteLine(Yellow($"Name: {status.Name}"))
                    .WriteLine(Yellow($"Instance: {status.OrchestrationInstance}"))
                    .WriteLine(Yellow($"Version: {status.Version}"))
                    .WriteLine(Yellow($"TimeCreated: {status.CreatedTime}"))
                    .WriteLine(Yellow($"CompletedTime: {status.CompletedTime}"))
                    .WriteLine(Yellow($"LastUpdatedTime: {status.LastUpdatedTime}"))
                    .WriteLine(Yellow($"Input: {status.Input}"))
                    .WriteLine(Yellow($"Output: {status.Output}"));

                if (status.OrchestrationStatus.ToString() == "Failed")
                {
                    ColoredConsole.WriteLine(Red($"Status: {status.OrchestrationStatus}"));
                }
                else if (status.OrchestrationStatus.ToString() == "Completed")
                {
                    ColoredConsole.WriteLine(Green($"Status: {status.OrchestrationStatus}"));
                }
                else
                {
                    ColoredConsole.WriteLine(Blue($"Status: {status.OrchestrationStatus}"));
                }
            }
        }
    }
}