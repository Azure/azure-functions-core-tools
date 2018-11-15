using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
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

        private static readonly string WorkingDirPath = Environment.GetEnvironmentVariable("DURABLE");

        private const string _storageReason = "Durable E2E tests need a storage account connection";

        public DurableTests(ITestOutputHelper output) : base(output) { }


        [SkippableFact]
        public async Task DurableDeleteTaskHubTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "deleteTaskHubTest";
            bool requiresHost = false;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
            
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
                    $"durable delete-task-hub --task-hub-name {taskHubName}",
                },
                OutputContains = new string[]
                {
                    "Task hub successfully deleted."
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurableGetHistoryTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "getHistoryTest";
            bool requiresHost = true;
            
            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
            
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
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
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurableGetInstancesTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "getInstancesTest";
            bool requiresHost = false;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
            
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
                    $"durable get-instances --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Continuation token for next set of results:"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurableGetRuntimeStatusTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "getRuntimeStatus";
            bool requiresHost = false;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
           
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
                    $"durable get-runtime-status --id {instanceId} --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    $"Could not find an orchestration instance with id '{instanceId}"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurablePurgeHistoryTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "purgeHistoryTest";
            bool requiresHost = true;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
            
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
                    $"durable purge-history --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Purged orchestration history for all instances created between '1/1/0001 12:00:00 AM' and '12/30/9999 11:59:59 PM'"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurableRaiseEventTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "raiseEventTest";
            bool requiresHost = false;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
            
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
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
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurableRewindTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "rewindTest";
            bool requiresHost = true;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
            
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
                    $"durable start-new --function-name Counter --input 3 --id {instanceId} --task-hub-name {taskHubName}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data baddata --task-hub-name {taskHubName}",
                    $"durable rewind --id {instanceId} --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Status before rewind: Failed",
                    "Status after rewind: Failed"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurableStartNewTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string taskHubName = "startNewTest";
            bool requiresHost = false;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }
            
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
                    $"durable start-new --function-name Counter --input 3 --task-hub-name {taskHubName}"
                },
                OutputContains = new string[]
                {
                    "Started 'Counter'"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }

        [SkippableFact]
        public async Task DurableTerminateTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";
            string taskHubName = "terminateTest";
            bool requiresHost = true;

            if (requiresHost)
            {
                DurableHelper.SetTaskHubName(WorkingDirPath, taskHubName);
            }

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                    {
                        $"settings add {DurableManager.DefaultConnectionStringKey} {StorageConnectionString}",
                        $"durable start-new --function-name Counter --id {instanceId} --task-hub-name {taskHubName}",
                        $"durable terminate --id {instanceId} --task-hub-name {taskHubName}"
                    },
                OutputContains = new string[]
                    {
                        $"Successfully terminated '{instanceId}'"
                    }
            }, 
            _output, 
            workingDir: WorkingDirPath,
            startHost: requiresHost);
        }
    }
}
