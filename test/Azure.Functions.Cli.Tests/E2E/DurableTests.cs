using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    /// <summary>
    /// Class of E2E tests for Durable Functions. These should not be run in parallel
    /// </summary>
    public class DurableTests : BaseE2ETest
    {
        private static readonly string StorageConnectionString = Environment.GetEnvironmentVariable("STORAGE_CONNECTION");

        private static readonly string WorkingDirPath = Environment.GetEnvironmentVariable("DURABLE");

        private const string _storageReason = "Durable E2E tests need a storage account connection";

        public DurableTests(ITestOutputHelper output) : base(output) { }

        [SkippableFact]
        public async Task DurableDeleteTaskHubTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable delete-task-hub --connection-string {StorageConnectionString}",
                },
                OutputContains = new string[]
                {
                    "Task hub successfully deleted."
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);
        }

        [SkippableFact]
        public async Task DurableGetHistoryTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --connection-string {StorageConnectionString} --id {instanceId}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data add --connection-string {StorageConnectionString}",
                    $"durable get-history --id {instanceId} --connection-string {StorageConnectionString}"
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
            startHost: true);
        }

        [SkippableFact]
        public async Task DurableGetInstancesTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable get-instances --connection-string {StorageConnectionString}"
                },
                OutputContains = new string[]
                {
                    "Continuation token for next set of results:"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);
        }

        [SkippableFact]
        public async Task DurableGetRuntimeStatusTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable get-runtime-status --id {instanceId} --connection-string {StorageConnectionString}"
                },
                OutputContains = new string[]
                {
                    $"Could not find an orchestration instance with id '{instanceId}"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);
        }

        [SkippableFact]
        public async Task DurablePurgeHistoryTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable purge-history --connection-string {StorageConnectionString}"
                },
                OutputContains = new string[]
                {
                    "Purged orchestration history of instances created between '1/1/0001 12:00:00 AM' and '12/30/9999 11:59:59 PM'"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: false);
        }

        [SkippableFact]
        public async Task DurableRaiseEventTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --input 3 --connection-string {StorageConnectionString} --id {instanceId}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data add --connection-string {StorageConnectionString}"
                },
                OutputContains = new string[]
                {
                    $"Raised event 'operation' to instance '{instanceId}'"
                }
            }, _output, workingDir: WorkingDirPath);
        }

        [SkippableFact]
        public async Task DurableRewindTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --input 3 --connection-string {StorageConnectionString} --id {instanceId}",
                    $"durable raise-event --id {instanceId} --event-name operation --event-data baddata --connection-string {StorageConnectionString}",
                    $"durable rewind --id {instanceId} --connection-string {StorageConnectionString}"
                },
                Test = async (workingDir, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                },
                OutputContains = new string[]
                {
                    "Status before rewind: Failed",
                    "Status after rewind: Failed"
                }
            },
            _output,
            workingDir: WorkingDirPath,
            startHost: true);
        }

        [SkippableFact]
        public async Task DurableStartNewTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"durable start-new --function-name Counter --input 3 --connection-string {StorageConnectionString}"
                },
                OutputContains = new string[]
                {
                    "Started 'Counter'"
                }
            },
            _output,
            workingDir: WorkingDirPath);
        }

        [SkippableFact]
        public async Task DurableTerminateTest()
        {
            Skip.If(string.IsNullOrEmpty(StorageConnectionString),
                reason: _storageReason);

            string instanceId = $"{Guid.NewGuid():N}";

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                    {
                        $"durable start-new --function-name Counter --connection-string {StorageConnectionString} --id {instanceId}",
                        $"durable terminate --id {instanceId} --connection-string {StorageConnectionString}"
                    },
                OutputContains = new string[]
                    {
                        $"Successfully terminated '{instanceId}'"
                    }
            }, 
            _output, 
            workingDir: WorkingDirPath,
            startHost: true);
        }
    }
}
