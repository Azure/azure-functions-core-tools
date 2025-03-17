using Cli.Core.E2E.Tests.Fixtures;
using Cli.Core.E2E.Tests.Traits;
using FluentAssertions;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.func_start.Tests.TestsWithFixtures
{
    public class DotnetIsolatedTests : IClassFixture<DotnetIsolatedFunctionAppFixture>
    {
        private readonly DotnetIsolatedFunctionAppFixture _fixture;

        public DotnetIsolatedTests(DotnetIsolatedFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }


        [Fact]
        public async Task Start_DotnetIsolated_Net9_SuccessfulFunctionExecution()
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
                    var response = await client.GetAsync($"http://localhost:{port}/api/HttpTrigger");
                    capturedContent = await response.Content.ReadAsStringAsync();

                    process.Kill(true);
                }
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Welcome to Azure Functions!");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_DotnetIsolated_WithRuntimeSpecified()
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
                        .Execute(new[] { "start", "--verbose", "--runtime", "default", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Welcome to Azure Functions!",
                because: "response from default function should be 'Welcome to Azure Functions!'");

            // Validate default host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected default host.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_DotnetIsolated_WithoutRuntimeSpecified()
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
            capturedContent.Should().Be("Welcome to Azure Functions!",
                because: "response from default function should be 'Welcome to Azure Functions!'");

            // Validate default host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        public async Task DontStart_InProc6_SpecifiedRuntime_ForDotnetIsolated()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start with invalid runtime (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--runtime", "inproc6", "--port", port.ToString() });

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }

        [Fact]
        public async Task DontStart_InProc8_SpecifiedRuntime_ForDotnetIsolated()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start with invalid runtime (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--runtime", "inproc8", "--port", port.ToString() });

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }
    }
}
