using Cli.Core.E2E.Tests.Fixtures;
using Cli.Core.E2E.Tests.Traits;
using FluentAssertions;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using TestFramework.Assertions;
using TestFramework.Attributes;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.func_start.Tests.TestsWithFixtures
{
    [Collection("Dotnet6InProc")]
    [Trait(TestTraits.Group, TestTraits.InProc)]
    public class Dotnet6InProcTests : IClassFixture<Dotnet6InProcFunctionAppFixture>
    {
        private readonly Dotnet6InProcFunctionAppFixture _fixture;

        public Dotnet6InProcTests(Dotnet6InProcFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net6_SuccessfulFunctionExecution_WithSpecifyingRuntime()
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
                        .Execute(new[] { "start", "--verbose", "--runtime", "inproc6", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.",
                because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

            // Validate inproc6 host was started
            result.Should().HaveStdOutContaining("Starting child process for inproc6 model host.");
            result.Should().HaveStdOutContaining("Selected inproc6 host.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net6_SuccessfulFunctionExecution_WithoutSpecifyingRuntime()
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

            // Validate inproc6 host was started
            result.Should().HaveStdOutContaining("Starting child process for inproc6 model host.");
            result.Should().HaveStdOutContaining("Selected inproc6 host.");
        }

        [Fact]
        public async Task Start_InProc_Dotnet6_WithoutSpecifyingRuntime_ExpectedToFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("Failed to locate the inproc6 model host at");
        }

        [Fact]
        public async Task Start_InProc_Dotnet6_WithSpecifyingRuntime_ExpectedToFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--runtime", "inproc6", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("Failed to locate the inproc6 model host at");
        }

        [Fact]
        public async Task DontStart_InProc8_SpecifiedRuntime_ForDotnet6InProc()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--runtime", "inproc8", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc8', is invalid. For the 'inproc8' runtime, the 'FUNCTIONS_INPROC_NET8_ENABLED' environment variable must be set. See https://aka.ms/azure-functions/dotnet/net8-in-process.");
        }

        [Fact]
        public async Task DontStart_DefaultRuntime_SpecifiedRuntime_ForDotnet6InProc()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--runtime", "default", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'default', is invalid. The provided value is only valid for the worker runtime 'dotnetIsolated'.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with invalid host.json
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_invalid_host");
            Directory.CreateDirectory(tempDir);
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Create invalid host.json
            string hostJsonPath = Path.Combine(tempDir, "host.json");
            string hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            // We are expecting an exit to happen gracefully here;
            // if func start were to succeed, the user would have to manually kill the process and exit with -1
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Extension bundle configuration should not be present");

            // Clean up temporary directory
            Directory.Delete(tempDir, true);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_MissingHostJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory without host.json
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_missing_host");
            Directory.CreateDirectory(tempDir);
            CopyDirectoryHelpers.CopyDirectoryWithout(_fixture.WorkingDirectory, tempDir, "host.json");

            // Call func start
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            // We are expecting an exit to happen gracefully here;
            // if func start were to succeed, the user would have to manually kill the process and exit with -1
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Host.json file in missing");

            // Clean up temporary directory
            Directory.Delete(tempDir, true);
        }
    }
}