// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.E2E.Tests.Fixtures;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncDurable
{
    [Collection("DurableFunctionTests")]
    public class DurableTests : IClassFixture<DurableFunctionAppFixture>
    {
        private const string StorageReason = "Durable tests need a storage account connection";
        private readonly DurableFunctionAppFixture _fixture;

        public DurableTests(DurableFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [SkippableFact]
        public void DeleteTaskHub_WithTaskHubName_ReturnsExpectedOutput()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var taskHubName = "deleteTaskHubTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(DeleteTaskHub_WithTaskHubName_ReturnsExpectedOutput), _fixture.Log);
            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["delete-task-hub", "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining($"Task hub '{taskHubName}' successfully deleted.");
        }

        [SkippableFact]
        public void GetInstances_WithTaskHubName_ReturnsExpectedOutput()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var taskHubName = "getInstancesTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(GetInstances_WithTaskHubName_ReturnsExpectedOutput), _fixture.Log);
            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["get-instances", "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Continuation token for next set of results:");
        }

        [SkippableFact]
        public void GetRuntimeStatus_WithUnknownInstanceId_ReturnsNotFoundMessage()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var instanceId = $"{Guid.NewGuid():N}";
            var taskHubName = "getRuntimeStatusTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(GetRuntimeStatus_WithUnknownInstanceId_ReturnsNotFoundMessage), _fixture.Log);
            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["get-runtime-status", "--id", instanceId, "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining($"Could not find an orchestration instance with id '{instanceId}");
        }

        [SkippableFact]
        public void PurgeHistory_WithValidTaskHubName_DisplaysSuccessfulExecution()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);
            var instanceId = $"{Guid.NewGuid():N}";
            var taskHubName = "purgeHistoryTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(PurgeHistory_WithValidTaskHubName_DisplaysSuccessfulExecution), _fixture.Log);

            // Start new orchestration
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "CounterTest", "--id", instanceId, "--task-hub-name", taskHubName])
                .Should().ExitWith(0)
                .HaveStdOutContaining("Started 'CounterTest'");

            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["purge-history", "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining($"Purged orchestration history for all instances created between '{DurableManager.CreatedAfterDefault}' and '{DurableManager.CreatedBeforeDefault}'");
        }

        [SkippableFact]
        public void RaiseEvent_WithValidEventData_RaisesEventSuccessfully()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var instanceId = $"{Guid.NewGuid():N}";
            var taskHubName = "raiseEventTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(RaiseEvent_WithValidEventData_RaisesEventSuccessfully), _fixture.Log);
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "Counter", "--input", "3", "--id", instanceId, "--task-hub-name", taskHubName]);

            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["raise-event", "--id", instanceId, "--event-name", "operation", "--event-data", "add", "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining($"Raised event 'operation' to instance '{instanceId}'");
        }

        [SkippableFact]
        public void RaiseEvent_WithFileInput_RaisesEventSuccessfully()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var taskHubName = "raiseEventFileInputTest";
            var filename = Path.Combine(_fixture.WorkingDirectory, "raiseEvent.json");
            var testObject = new { Name = "RaiseEvent", Hello = "World" };
            File.WriteAllText(filename, JsonConvert.SerializeObject(testObject));
            var instanceId = $"{Guid.NewGuid():N}";
            var eventName = "parse";
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, _fixture.StorageConnectionString);

            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(RaiseEvent_WithFileInput_RaisesEventSuccessfully), _fixture.Log);

            // Start new orchestration
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "JsonInput", "--id", instanceId, "--task-hub-name", taskHubName])
                .Should().ExitWith(0);

            // Raise event with file input
            var raiseResult = funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["raise-event", "--id", instanceId, "--event-name", eventName, "--event-data", "@raiseEvent.json", "--task-hub-name", taskHubName]);
            raiseResult.Should().ExitWith(0);
            raiseResult.Should().HaveStdOutContaining($"Raised event '{eventName}' to instance '{instanceId}'");

            // Get runtime status
            var statusResult = funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["get-runtime-status", "--id", instanceId, "--task-hub-name", taskHubName]);
            statusResult.Should().ExitWith(0);
            statusResult.Should().HaveStdOutContaining("\"OrchestrationStatus\": 6");

            File.Delete(filename);
            Environment.SetEnvironmentVariable(DurableManager.DefaultConnectionStringKey, null);
        }

        [SkippableFact]
        public void StartNewInstance_WithValidInput_StartsInstanceSuccessfully()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var taskHubName = "startNewTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(StartNewInstance_WithValidInput_StartsInstanceSuccessfully), _fixture.Log);
            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "Counter", "--input", "3", "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Started 'Counter'");
        }

        [SkippableFact]
        public void StartNewInstance_WithFileInput_StartsInstanceSuccessfully()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var taskHubName = "startNewFileInputTest";
            var filename = Path.Combine(_fixture.WorkingDirectory, "startnew.json");
            var testObject = new { Name = "StartNew", Hello = "World" };
            File.WriteAllText(filename, JsonConvert.SerializeObject(testObject));
            var instanceId = $"{Guid.NewGuid():N}";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(StartNewInstance_WithFileInput_StartsInstanceSuccessfully), _fixture.Log);

            // Start new orchestration with file input
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "JsonInput", "--input", "@startnew.json", "--task-hub-name", taskHubName, "--id", instanceId])
                .Should().ExitWith(0)
                .HaveStdOutContaining("Started 'JsonInput'");

            // Get runtime status
            var result = funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["get-runtime-status", "--id", instanceId, "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("\"OrchestrationStatus\": 6");
            File.Delete(filename);
        }

        [SkippableFact]
        public void GetHistory_WithValidInstanceAndTaskHubName_ReturnsExpectedOutput()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var instanceId = $"{Guid.NewGuid():N}";
            var taskHubName = "getHistoryTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(GetHistory_WithValidInstanceAndTaskHubName_ReturnsExpectedOutput), _fixture.Log);

            // Start new orchestration
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "Counter", "--id", instanceId, "--task-hub-name", taskHubName])
                .Should().ExitWith(0)
                .HaveStdOutContaining("Started 'Counter'");

            // Raise event
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["raise-event", "--id", instanceId, "--event-name", "operation", "--event-data", "add", "--task-hub-name", taskHubName])
                .Should().ExitWith(0);

            // Get history
            var result = funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["get-history", "--id", instanceId, "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("OrchestratorStarted");
            result.Should().HaveStdOutContaining("ExecutionStarted");
            result.Should().HaveStdOutContaining("OrchestratorCompleted");
        }

        [SkippableFact]
        public void Rewind_WithValidInstanceAndTaskHubName_DisplaysExpectedOutput()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var instanceId = $"{Guid.NewGuid():N}";
            var taskHubName = "rewindTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(Rewind_WithValidInstanceAndTaskHubName_DisplaysExpectedOutput), _fixture.Log);
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "Counter", "--input", "3", "--id", instanceId, "--task-hub-name", taskHubName]);
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["raise-event", "--id", instanceId, "--event-name", "operation", "--event-data", "baddata", "--task-hub-name", taskHubName]);

            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["rewind", "--id", instanceId, "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Status before rewind: Failed");
            result.Should().HaveStdOutContaining("Status after rewind:");
        }

        [SkippableFact]
        public void TerminateInstance_WithTaskHubName_DisplaysSuccessMessage()
        {
            Skip.If(string.IsNullOrEmpty(_fixture.StorageConnectionString), StorageReason);

            var instanceId = $"{Guid.NewGuid():N}";
            var taskHubName = "terminateTest";
            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(TerminateInstance_WithTaskHubName_DisplaysSuccessMessage), _fixture.Log);
            funcDurableCommand.WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["start-new", "--function-name", "Counter", "--id", instanceId, "--task-hub-name", taskHubName]);

            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["terminate", "--id", instanceId, "--task-hub-name", taskHubName]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining($"Successfully terminated '{instanceId}'");
        }
    }
}
