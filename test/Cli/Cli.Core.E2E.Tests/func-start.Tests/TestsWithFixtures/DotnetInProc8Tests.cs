using Cli.Core.E2E.Tests.Fixtures;
using Cli.Core.E2E.Tests.Traits;
using FluentAssertions;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using TestFramework.Assertions;
using TestFramework.Attributes;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Azure.AppService.Proxy.Runtime.Trace;

namespace Cli.Core.E2E.Tests.func_start.Tests.TestsWithFixtures
{
    [Collection("Dotnet8InProc")]
    [Trait(TestTraits.Group, TestTraits.InProc)]
    public class Dotnet8InProcTests : IClassFixture<Dotnet8InProcFunctionAppFixture>
    {
        private readonly Dotnet8InProcFunctionAppFixture _fixture;

        public Dotnet8InProcTests(Dotnet8InProcFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_Net8_SuccessfulFunctionExecution_WithoutSpecifyingRuntime()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);
            string capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, port);
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://localhost:{port}/api/HttpTrigger?name=Test");
                    capturedContent = await response.Content.ReadAsStringAsync();
                    process.Kill(true);
                }
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "start", "--verbose", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.",
                because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

            // Validate inproc8 host was started
            result.Should().HaveStdOutContaining("Starting child process for inproc8 model host.");
            result.Should().HaveStdOutContaining("Selected inproc8 host.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_Net8_SuccessfulFunctionExecution_WithSpecifyingRuntime()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);
            string capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, port);
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://localhost:{port}/api/HttpTrigger?name=Test");
                    capturedContent = await response.Content.ReadAsStringAsync();
                    process.Kill(true);
                }
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "--verbose", "--runtime", "inproc8", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.",
                because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

            // Validate inproc8 host was started
            result.Should().HaveStdOutContaining("Starting child process for inproc8 model host.");
            result.Should().HaveStdOutContaining("Selected inproc8 host.");
        }

        [Fact]
        public async Task Start_Net8InProc_ExpectedToFail_WithSpecifyingRuntime()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "start", "--verbose", "--runtime", "inproc8", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("Failed to locate the inproc8 model host");
        }

        [Fact]
        public async Task Start_Net8InProc_ExpectedToFail_WithoutSpecifyingRuntime()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "start", "--verbose", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("Failed to locate the inproc8 model host");
        }

        [Fact]
        public async Task DontStart_InProc6_SpecifiedRuntime_ForDotnet8InProc()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "start", "--verbose", "--runtime", "inproc6", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. For the 'inproc6' runtime, the 'FUNCTIONS_INPROC_NET8_ENABLED' environment variable cannot be be set. See https://aka.ms/azure-functions/dotnet/net8-in-process.");
        }

        [Fact]
        public async Task DontStart_DefaultRuntime_SpecifiedRuntime_ForDotnet8InProc()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "start", "--verbose", "--runtime", "default", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'default', is invalid. The provided value is only valid for the worker runtime 'dotnetIsolated'.");
        }
    }
}