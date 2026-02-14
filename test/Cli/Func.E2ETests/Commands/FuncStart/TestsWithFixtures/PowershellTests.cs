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

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.TestsWithFixtures
{
    [Collection("Powershell")]
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Powershell)]
    public class PowershellTests : IClassFixture<PowershellFunctionAppFixture>
    {
        private readonly PowershellFunctionAppFixture _fixture;

        public PowershellTests(PowershellFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Start_PowershellApp_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_PowershellApp_SuccessfulFunctionExecution), _fixture.Log);
            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };
            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "powershell")
                        .Execute(["--verbose", "--port", port.ToString()]);

            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate out-of-process host was started
            result.Should().StartOutOfProcessHost();
        }

        [Theory]
        [InlineData("false", true)] // EnsureLatest=false: func start should download
        [InlineData("true", false)] // EnsureLatest=true: host handles download, skip
        public void FuncStart_WithEnsureLatestEnvVar_ShowsExpectedBehavior(string ensureLatestValue, bool shouldDownload)
        {
            BaseOfflineBundleTests.TestEnsureLatestBehavior(
                _fixture.FuncPath,
                _fixture.WorkingDirectory,
                "powershell",
                _fixture.Log,
                ensureLatestValue,
                shouldDownload,
                configSource: EnsureLatestConfigSource.EnvironmentVariable);
        }

        [Theory]
        [InlineData("false", true)] // EnsureLatest=false in host.json: func start should download
        [InlineData("true", false)] // EnsureLatest=true in host.json: host handles download, skip
        public void FuncStart_WithEnsureLatestHostJson_ShowsExpectedBehavior(string ensureLatestValue, bool shouldDownload)
        {
            BaseOfflineBundleTests.TestEnsureLatestBehavior(
                _fixture.FuncPath,
                _fixture.WorkingDirectory,
                "powershell",
                _fixture.Log,
                ensureLatestValue,
                shouldDownload,
                configSource: EnsureLatestConfigSource.HostJson);
        }
    }
}
