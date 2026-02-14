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
    [Collection("NodeV3")]
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodeV3Tests : IClassFixture<NodeV3FunctionAppFixture>
    {
        private readonly NodeV3FunctionAppFixture _fixture;

        public NodeV3Tests(NodeV3FunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Start_NodeJsApp_V3_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_NodeJsApp_V3_SuccessfulFunctionExecution), _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                        .Execute(["--verbose", "--port", port.ToString()]);

            capturedContent.Should().Be("This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response.");
        }

        [Theory]
        [InlineData("false", true)] // EnsureLatest=false: func start should download
        [InlineData("true", false)] // EnsureLatest=true: host handles download, skip
        public void FuncStart_NodeV3_WithEnsureLatestEnvVar_ShowsExpectedBehavior(string ensureLatestValue, bool shouldDownload)
        {
            BaseOfflineBundleTests.TestEnsureLatestBehavior(
                _fixture.FuncPath,
                _fixture.WorkingDirectory,
                "node",
                _fixture.Log,
                ensureLatestValue,
                shouldDownload,
                "v3",
                EnsureLatestConfigSource.EnvironmentVariable);
        }

        [Theory]
        [InlineData("false", true)] // EnsureLatest=false in host.json: func start should download
        [InlineData("true", false)] // EnsureLatest=true in host.json: host handles download, skip
        public void FuncStart_NodeV3_WithEnsureLatestHostJson_ShowsExpectedBehavior(string ensureLatestValue, bool shouldDownload)
        {
            BaseOfflineBundleTests.TestEnsureLatestBehavior(
                _fixture.FuncPath,
                _fixture.WorkingDirectory,
                "node",
                _fixture.Log,
                ensureLatestValue,
                shouldDownload,
                "v3",
                EnsureLatestConfigSource.HostJson);
        }
    }
}
