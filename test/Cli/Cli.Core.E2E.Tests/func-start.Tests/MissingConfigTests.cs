using Cli.Core.E2E.Tests.Traits;
using FluentAssertions;
using System.Net;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests
{
    public class MissingConfigTests : BaseE2ETest
    {
        public MissingConfigTests(ITestOutputHelper log) : base(log)
        {
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app
            var funcInitResult = new FuncInitCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--worker-runtime", "dotnet" });
            funcInitResult.Should().ExitWith(0);

            // Add HTTP trigger
            var funcNewResult = new FuncNewCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });
            funcNewResult.Should().ExitWith(0);

            // Create invalid host.json
            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            string hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var result = new FuncStartCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            result.Should().HaveStdOutContaining("Extension bundle configuration should not be present");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_MissingHostJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app
            var funcInitResult = new FuncInitCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--worker-runtime", "dotnet" });
            funcInitResult.Should().ExitWith(0);

            // Add HTTP trigger
            var funcNewResult = new FuncNewCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });
            funcNewResult.Should().ExitWith(0);

            // Delete host.json
            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            File.Delete(hostJsonPath);

            // Call func start
            var result = new FuncStartCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            result.Should().HaveStdOutContaining("Host.json file in missing");
        }

        [Theory]
        [InlineData("dotnet-isolated", "--dotnet-isolated", true, false)]
        [InlineData("node", "--node", true, false)]
        [InlineData("dotnet-isolated", "", true, true)]
        public async Task Start_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, bool invokeFunction, bool setRuntimeViaEnvironment)
        {
            try
            {
                if (setRuntimeViaEnvironment)
                {
                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated");
                }

                int port = ProcessHelper.GetAvailablePort();

                // Initialize function app
                var funcInitResult = new FuncInitCommand(FuncPath, Log)
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(new[] { ".", "--worker-runtime", language });
                funcInitResult.Should().ExitWith(0);

                // Add HTTP trigger
                await RetryHelper.RetryAsync(
                 () => {
                     var funcNewResult = new FuncNewCommand(FuncPath, Log)
                         .WithWorkingDirectory(WorkingDirectory)
                         .Execute(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerFunc" });

                     // Return true if successful (exit code 0), false if we should retry
                     return Task.FromResult(funcNewResult.ExitCode == 0);
                 },
                 timeout: 60 * 1000, // 60 seconds timeout
                 pollingInterval: 3 * 1000, // Retry every 3 seconds
                 userMessageCallback: () => $"Failed to create HTTP trigger"
             );

                // Delete local.settings.json
                var localSettingsJson = Path.Combine(WorkingDirectory, "local.settings.json");
                File.Delete(localSettingsJson);

                // Call func start
                var funcStartCommand = new FuncStartCommand(FuncPath, Log);
                string capturedContent = null;

                funcStartCommand.ProcessStartedHandler = async process =>
                {
                    try
                    {
                        if (invokeFunction)
                        {
                            await ProcessHelper.WaitForFunctionHostToStart(process, port);
                            using (var client = new HttpClient())
                            {
                                var response = await client.GetAsync($"http://localhost:{port}/api/HttpTriggerFunc?name=Test");
                                response.StatusCode.Should().Be(HttpStatusCode.OK);
                                capturedContent = await response.Content.ReadAsStringAsync();
                            }
                        }
                    }
                    finally
                    {
                        process.Kill(true);
                    }
                };

                var startCommand = new List<string> { "--port", port.ToString() };
                if (!string.IsNullOrEmpty(runtimeParameter))
                {
                    startCommand.Add(runtimeParameter);
                }

                var result = funcStartCommand
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(startCommand.ToArray());

                // Validate output contains expected function URL
                if (invokeFunction)
                {
                    result.Should().HaveStdOutContaining("HttpTriggerFunc: [GET,POST] http://localhost:");
                }
            }
            finally
            {
                // Clean up environment variable
                if (setRuntimeViaEnvironment)
                {
                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", null);
                }
            }
        }

        [Fact]
        public async Task Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();
            var functionName = "HttpTriggerJS";

            // Initialize Node.js function app
            var funcInitResult = new FuncInitCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--worker-runtime", "node", "-m", "v3" });
            funcInitResult.Should().ExitWith(0);

            // Add HTTP trigger
            await RetryHelper.RetryAsync(
                () => {
                    var funcNewResult = new FuncNewCommand(FuncPath, Log)
                        .WithWorkingDirectory(WorkingDirectory)
                        .Execute(new[] { ".", "--template", "Httptrigger", "--name", functionName });

                    // Return true if successful (exit code 0), false if we should retry
                    return Task.FromResult(funcNewResult.ExitCode == 0);
                },
                timeout: 60 * 1000, // 60 seconds timeout
                pollingInterval: 3 * 1000, // Retry every 3 seconds
                userMessageCallback: () => $"Failed to create HTTP trigger"
            );

            // Modify function.json to include an invalid binding type
            var filePath = Path.Combine(WorkingDirectory, functionName, "function.json");
            var functionJson = await File.ReadAllTextAsync(filePath);
            functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
            await File.WriteAllTextAsync(filePath, functionJson);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                // Wait for error to appear
                await ProcessHelper.WaitForFunctionHostToStart(process, port);
                // Kill the process
                process.Kill(true);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            result.Should().HaveStdOutContaining("The binding type(s) 'http2' were not found in the configured extension bundle. Please ensure the type is correct and the correct version of extension bundle is configured.");
        }
    }
}