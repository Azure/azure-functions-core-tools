using Cli.Core.E2E.Tests.Fixtures;
using Cli.Core.E2E.Tests.Traits;
using FluentAssertions;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.func_start.Tests.TestsWithFixtures
{
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
        public async Task Start_InProc_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with modified host.json
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_modified_host");
            Directory.CreateDirectory(tempDir);
            CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Modify host.json to set log level to Debug
            string hostJsonPath = Path.Combine(tempDir, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await Task.Delay(5000); // Give some time for logs to appear
                process.Kill(true);
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "start", "--port", port.ToString() });

            // Validate host configuration was applied
            result.Should().HaveStdOutContaining("Host configuration applied.");

            // Clean up temporary directory
            Directory.Delete(tempDir, true); 
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with modified host.json
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_modified_host_filter");
            Directory.CreateDirectory(tempDir);
            CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Modify host.json to set log level with filter
            string hostJsonPath = Path.Combine(tempDir, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\", \"Host.Startup\": \"Information\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await Task.Delay(5000); // Give some time for logs to appear
                process.Kill(true);
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "start", "--port", port.ToString() });

            // Validate we see some logs but not others due to filters
            result.Should().HaveStdOutContaining("Found the following functions:");
            result.Should().NotHaveStdOutContaining("Reading host configuration file");

            // Clean up temporary directory
            Directory.Delete(tempDir, true);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with invalid host.json
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_invalid_host");
            Directory.CreateDirectory(tempDir);
            CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Create invalid host.json
            string hostJsonPath = Path.Combine(tempDir, "host.json");
            string hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "start", "--port", port.ToString() });

            // Validate error message
            result.Should().ExitWith(1);
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
            CopyDirectoryWithout(_fixture.WorkingDirectory, tempDir, "host.json");

            // Call func start
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "start", "--port", port.ToString() });

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdOutContaining("Host.json file in missing");

            // Clean up temporary directory
            try { Directory.Delete(tempDir, true); } catch { }
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_Dotnet_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError()
        {
            // Skip if AzureWebJobsStorage is set
            string azureWebJobsStorage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (!string.IsNullOrEmpty(azureWebJobsStorage))
            {
                _fixture.Log.WriteLine("Skipping test as AzureWebJobsStorage is set");
                return;
            }

            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with modified files
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_user_secrets");
            Directory.CreateDirectory(tempDir);
            CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Add Queue trigger function
            var funcNewResult = new FuncNewCommand(_fixture.FuncPath, _fixture.Log)
                                .WithWorkingDirectory(tempDir)
                                .Execute(new List<string> { "--template", "Queue trigger", "--name", "queue1" });
            funcNewResult.Should().ExitWith(0);

            // Modify queue code to use connection string
            string queueCodePath = Path.Combine(tempDir, "queue1.cs");
            string queueCode = File.ReadAllText(queueCodePath);
            queueCode = queueCode.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
            File.WriteAllText(queueCodePath, queueCode);

            // Clear local.settings.json
            string settingsPath = Path.Combine(tempDir, "local.settings.json");
            string settingsContent = "{ \"IsEncrypted\": false, \"Values\": { \"FUNCTIONS_WORKER_RUNTIME\": \"dotnet\"} }";
            File.WriteAllText(settingsPath, settingsContent);

            // Set up user secrets
            SetupUserSecrets(tempDir, new Dictionary<string, string>
            {
                { "ConnectionStrings:MyQueueConn", "UseDevelopmentStorage=true" }
            });

            // Call func start for HTTP function only
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "start", "--functions", "http1", "--port", port.ToString() });

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdOutContaining("Missing value for AzureWebJobsStorage in local.settings.json");
            result.Should().HaveStdOutContaining("A host error has occurred during startup operation");

            // Clean up temporary directory
            Directory.Delete(tempDir, true);
        }

        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Create all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
            }

            // Copy all files
            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string destFile = filePath.Replace(sourceDir, destinationDir);
                File.Copy(filePath, destFile, true);
            }
        }

        private void CopyDirectoryWithout(string sourceDir, string destinationDir, string excludeFile)
        {
            // Create all subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
            }

            // Copy all files except the excluded one
            foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                if (!Path.GetFileName(filePath).Equals(excludeFile, StringComparison.OrdinalIgnoreCase))
                {
                    string destFile = filePath.Replace(sourceDir, destinationDir);
                    File.Copy(filePath, destFile, true);
                }
            }
        }

        private void SetupUserSecrets(string workingDir, Dictionary<string, string> secrets)
        {
            // Initialize user secrets
            var initProcess = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "user-secrets init",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using (var process = Process.Start(initProcess))
            {
                process.WaitForExit();
            }

            // Set each secret
            foreach (var secret in secrets)
            {
                var setProcess = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"user-secrets set \"{secret.Key}\" \"{secret.Value}\"",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(setProcess))
                {
                    process.WaitForExit();
                }
            }
        }
    }
}