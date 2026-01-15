// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core;
using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart
{
    /// <summary>
    /// Tests for log level configuration using pre-built fixtures.
    /// </summary>
    public class LogLevelTests : IClassFixture<PreBuiltDotnetIsolatedFixture>, IClassFixture<PreBuiltNodeFixture>
    {
        private readonly ITestOutputHelper _log;
        private readonly PreBuiltDotnetIsolatedFixture _dotnetFixture;
        private readonly PreBuiltNodeFixture _nodeFixture;

        public LogLevelTests(
            ITestOutputHelper log,
            PreBuiltDotnetIsolatedFixture dotnetFixture,
            PreBuiltNodeFixture nodeFixture)
        {
            _log = log;
            _dotnetFixture = dotnetFixture;
            _nodeFixture = nodeFixture;
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        public async Task Start_Dotnet_Isolated_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            var testName = nameof(Start_Dotnet_Isolated_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue);

            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}");
            CopyDirectoryHelpers.CopyDirectory(_dotnetFixture.WorkingDirectory, workingDir);

            try
            {
                await LogLevelTestHelpers.RunLogLevelOverridenViaHostJsonTest(
                    _dotnetFixture.FuncPath, workingDir, _log, testName);
            }
            finally
            {
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        public async Task Start_Dotnet_Isolated_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue()
        {
            var testName = nameof(Start_Dotnet_Isolated_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue);

            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}");
            CopyDirectoryHelpers.CopyDirectory(_dotnetFixture.WorkingDirectory, workingDir);

            try
            {
                await LogLevelTestHelpers.RunLogLevelOverridenWithFilterTest(
                    _dotnetFixture.FuncPath, workingDir, _log, testName);
            }
            finally
            {
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public void Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue);

            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}");
            CopyDirectoryHelpers.CopyDirectory(_nodeFixture.WorkingDirectory, workingDir);

            try
            {
                // Add debug log level setting
                var funcSettingsResult = new FuncSettingsCommand(_nodeFixture.FuncPath, testName, _log)
                                        .WithWorkingDirectory(workingDir)
                                        .Execute(["add", "AzureFunctionsJobHost__logging__logLevel__Default", "Debug"]);
                funcSettingsResult.Should().ExitWith(0);

                // Call func start
                var funcStartCommand = new FuncStartCommand(_nodeFixture.FuncPath, testName, _log);
                funcStartCommand.ProcessStartedHandler = async (process) =>
                {
                    await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
                };
                var result = funcStartCommand
                            .WithWorkingDirectory(workingDir)
                            .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                            .Execute(["--port", port.ToString(), "--verbose"]);

                // Validate we see detailed worker logs
                result.Should().HaveStdOutContaining("\"OriginalFunctionWorkerRuntime\": \"node\",");
            }
            finally
            {
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public void Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue);

            // Create a unique subdirectory for this test
            var workingDir = Path.Combine(Path.GetTempPath(), $"loglevel_test_{Guid.NewGuid():N}");
            CopyDirectoryHelpers.CopyDirectory(_nodeFixture.WorkingDirectory, workingDir);

            try
            {
                // Modify host.json to set log level to Warning (so we see important messages but not verbose ones)
                var hostJsonPath = Path.Combine(workingDir, "host.json");
                var hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Warning\"}}}";
                File.WriteAllText(hostJsonPath, hostJsonContent);

                // Call func start
                var funcStartCommand = new FuncStartCommand(_nodeFixture.FuncPath, testName, _log);
                funcStartCommand.ProcessStartedHandler = async (process) =>
                {
                    await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
                };
                var result = funcStartCommand
                            .WithWorkingDirectory(workingDir)
                            .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                            .Execute(["--port", port.ToString()]);

                // Validate that log level is respected - should see worker initialized but not verbose route logs
                result.Should().HaveStdOutContaining("Worker process started and initialized");
                result.Should().NotHaveStdOutContaining("Initializing function HTTP routes");
            }
            finally
            {
                try
                {
                    Directory.Delete(workingDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
