using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.DurableActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    /// <summary>
    /// Class of E2E tests for Durable Functions
    /// </summary>
    public class DurableTests : BaseE2ETest
    {
        private static readonly string StorageConnectionString = Environment.GetEnvironmentVariable("DURABLE_STORAGE_CONNECTION");

        private static readonly string WorkingDirPath = Environment.GetEnvironmentVariable("DURABLE_FUNCTION_PATH");

        private const string _storageReason = "Durable E2E tests need a storage account connection";

        public DurableTests(ITestOutputHelper output) : base(output) { }


        [SkippableFact]
        public async Task DurableDeleteTaskHubTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "deleteTaskHubTest";

            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable delete-task-hub --task-hub-name {taskHubName}",
                },
                OutputContains = new string[]
                {
                    $"Task hub '{taskHubName}' successfully deleted."
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurableGetHistoryTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "getHistoryTest";

            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --id {instanceId} --task-hub-name {taskHubName}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data add --task-hub-name {taskHubName}",
                    $"durable get-history --id {instanceId} --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Started 'Counter'",
                    "OrchestratorStarted",
                    "ExecutionStarted",
                    "OrchestratorCompleted"
                },
                CommandTimeout = TimeSpan.FromMinutes(2)
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: true);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurableGetInstancesTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "getInstancesTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable get-instances --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Continuation token for next set of results:"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurableGetRuntimeStatusTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "getRuntimeStatusTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable get-runtime-status --id {instanceId} --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    $"Could not find an orchestration instance with id '{instanceId}"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurablePurgeHistoryTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "purgeHistoryTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable purge-history --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    $"Purged orchestration history for all instances created between '{DurableManager.CreatedAfterDefault}' and '{DurableManager.CreatedBeforeDefault}'"
                },
                CommandTimeout = TimeSpan.FromSeconds(45)
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: true);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurableRaiseEventTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "raiseEventTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --input 3 --id {instanceId} --task-hub-name {taskHubName}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data add --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    $"Raised event 'operation' to instance '{instanceId}'"
                }
            }, 
            _output, 
            workingDir: WorkingDirPath,
            startHost: false);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurableRaiseEventTest_FileInput()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "raiseEventFileInputTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            string filename = Path.Combine(WorkingDirPath, "raiseEvent.json");
            var testObject = new
            {
                Name = "RaiseEvent",
                Hello = "World"
            };

            File.WriteAllText(filename, JsonConvert.SerializeObject(testObject));

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name JsonInput --id {instanceId} --task-hub-name {taskHubName}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data @raiseEvent.json --task-hub-name {taskHubName}",
                    $"durable get-runtime-status --id {instanceId}"
                },
                OutputContains = new string[]
                {
                    $"Raised event 'operation' to instance '{instanceId}'",
                    "\"OrchestrationStatus\": 0",
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: true);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);

            File.Delete(filename);
        }

        [SkippableFact]
        public async Task DurableRewindTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "rewindTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --input 3 --id {instanceId} --task-hub-name {taskHubName}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data baddata --task-hub-name {taskHubName}",
                    $"durable rewind --id {instanceId} --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Status before rewind: Failed",
                    "Status after rewind:"
                },
                CommandTimeout = TimeSpan.FromSeconds(45)
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: true);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurableStartNewTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "startNewTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --input 3 --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Started 'Counter'"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public async Task DurableStartNewTest_FileInput()
        {
            Skip.If(true, reason: "This test fails intermittently, needs to be fixed");
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "startNewFileInputTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            string filename = Path.Combine(WorkingDirPath, "startnew.json");
            var testObject = new
            {
                Name = "StartNew",
                Hello = "World"
            };

            File.WriteAllText(filename, JsonConvert.SerializeObject(testObject));

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name JsonInput --input @startnew.json --task-hub-name {taskHubName} --id {instanceId}",
                    $"durable get-runtime-status --id {instanceId}"
                },
                OutputContains = new string[]
                {
                    "Started 'JsonInput'",
                    "\"OrchestrationStatus\": 0",
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: true);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);

            File.Delete(filename);
        }

        [SkippableFact]
        public async Task DurableTerminateTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "terminateTest";
            DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, StorageConnectionString);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --id {instanceId} --task-hub-name {taskHubName}",
                    $"durable terminate --id {instanceId} --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    $"Successfully terminated '{instanceId}'"
                },
                CommandTimeout = TimeSpan.FromSeconds(45)
            }, 
            _output, 
            workingDir: WorkingDirPath,
            startHost: true);

            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }
    }
}
