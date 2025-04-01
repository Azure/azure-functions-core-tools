//using FluentAssertions;
//using Func.E2ETests.Traits;
//using Func.TestFramework.Assertions;
//using Func.TestFramework.Commands;
//using Func.TestFramework.Helpers;
//using Xunit.Abstractions;
//using Xunit;
//using Grpc.Net.Client.Configuration;
//using System.Reflection;

//namespace Func.E2ETests.func_start.Tests
//{
//    public class MissingConfigTests : BaseE2ETest
//    {
//        public MissingConfigTests(ITestOutputHelper log) : base(log)
//        {
//        }

//        [Fact]
//        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
//        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
//        {
//            int port = ProcessHelper.GetAvailablePort();

//            // Initialize dotnet function app using retry helper
//            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "dotnet" });

//            // Add HTTP trigger using retry helper
//            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });

//            // Create invalid host.json
//            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
//            string hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
//            File.WriteAllText(hostJsonPath, hostJsonContent);

//            // Call func start
//            var result = new FuncStartCommand(FuncPath, Log)
//                .WithWorkingDirectory(WorkingDirectory)
//                .Execute(new[] { "--port", port.ToString() });

//            // Validate error message
//            result.Should().HaveStdOutContaining("Extension bundle configuration should not be present");
//        }

//        [Fact]
//        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
//        public async Task Start_InProc_MissingHostJson_FailsWithExpectedError()
//        {
//            int port = ProcessHelper.GetAvailablePort();

//            // Initialize dotnet function app using retry helper
//            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "dotnet" });

//            // Add HTTP trigger using retry helper
//            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });

//            // Delete host.json
//            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
//            File.Delete(hostJsonPath);

//            // Call func start
//            var result = new FuncStartCommand(FuncPath, Log)
//                .WithWorkingDirectory(WorkingDirectory)
//                .Execute(new[] { "--port", port.ToString() });

//            // Validate error message
//            result.Should().HaveStdOutContaining("Host.json file in missing");
//        }

//        [Theory]
//        [InlineData("dotnet-isolated", "--dotnet-isolated", true, false)]
//        [InlineData("node", "--node", true, false)]
//        [InlineData("dotnet-isolated", "", true, true)]
//        public async Task Start_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, bool invokeFunction, bool setRuntimeViaEnvironment)
//        {
//            try
//            {
//                string methodName = "Start_MissingLocalSettingsJson_BehavesAsExpected";
//                string logFileName = $"{methodName}_{language}_{runtimeParameter}";
//                if (setRuntimeViaEnvironment)
//                {
//                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated");
//                }

//                int port = ProcessHelper.GetAvailablePort();

//                // Initialize function app using retry helper
//                await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", language });

//                // Add HTTP trigger using retry helper
//                await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerFunc" });

//                // Delete local.settings.json
//                var localSettingsJson = Path.Combine(WorkingDirectory, "local.settings.json");
//                File.Delete(localSettingsJson);

//                // Call func start
//                var funcStartCommand = new FuncStartCommand(FuncPath, Log, logFileName);



//                /*
//                funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
//                {
//                    await ProcessHelper.ProcessStartedHandlerHelper(port, process, Log, fileWriter, "HttpTriggerFunc");
//                };
//                */

//                string outputFromFunction = "";

//                funcStartCommand.CommandOutputHandler = async output =>
//                {
//                    outputFromFunction += output;
//                };

//                funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
//                {
//                    fileWriter?.WriteLine("[HANDLER] Handler started at " + DateTime.Now);
//                    fileWriter?.Flush();

//                    try
//                    {
//                        await RetryHelper.RetryAsync(async () =>
//                        {
//                            if (outputFromFunction.Contains("Worker process started and initialized."))
//                            {
//                                return true;
//                            }
//                            return false;
//                        });
//                    }
//                    finally
//                    {
//                        process.Kill();
//                    }
//                };

//                var startCommand = new List<string> { "--port", port.ToString(), "--verbose" };
//                if (!string.IsNullOrEmpty(runtimeParameter))
//                {
//                    startCommand.Add(runtimeParameter);
//                }

//                var result = funcStartCommand
//                    .WithWorkingDirectory(WorkingDirectory)
//                    .Execute(startCommand.ToArray());

//                // Validate output contains expected function URL
//                if (invokeFunction)
//                {
//                    result.Should().HaveStdOutContaining("HttpTriggerFunc: [GET,POST] http://localhost:");
//                }
//                result.Should().HaveStdOutContaining("Executed 'Functions.HttpTriggerFunc' (Succeeded");
//            }
//            finally
//            {
//                // Clean up environment variable
//                if (setRuntimeViaEnvironment)
//                {
//                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", null);
//                }
//            }
//        }

//        [Fact]
//        public async Task Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError()
//        {
//            int port = ProcessHelper.GetAvailablePort();
//            var functionName = "HttpTriggerJS";

//            // Initialize Node.js function app using retry helper
//            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "node", "-m", "v3" });

//            // Add HTTP trigger using retry helper
//            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", functionName });

//            // Modify function.json to include an invalid binding type
//            var filePath = Path.Combine(WorkingDirectory, functionName, "function.json");
//            var functionJson = await File.ReadAllTextAsync(filePath);
//            functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
//            await File.WriteAllTextAsync(filePath, functionJson);

//            // Call func start
//            var funcStartCommand = new FuncStartCommand(FuncPath, Log, "Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError");

//            /*
//            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
//            {
//                await ProcessHelper.ProcessStartedHandlerHelper(port, process, Log, fileWriter);
//            };
//            */

//            string outputFromFunction = "";

//            funcStartCommand.CommandOutputHandler = async output =>
//            {
//                outputFromFunction += output;
//            };

//            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
//            {
//                fileWriter?.WriteLine("[HANDLER] Handler started at " + DateTime.Now);
//                fileWriter?.Flush();

//                try
//                {
//                    await RetryHelper.RetryAsync(async () =>
//                    {
//                        if (outputFromFunction.Contains("Worker process started and initialized."))
//                        {
//                            return true;
//                        }
//                        return false;
//                    });
//                }
//                finally
//                {
//                    process.Kill();
//                }
//            };

//            var result = funcStartCommand
//                .WithWorkingDirectory(WorkingDirectory)
//                .Execute(new[] { "--port", port.ToString(), "--verbose" });

//            // Validate error message
//            result.Should().HaveStdOutContaining("The binding type(s) 'http2' were not found in the configured extension bundle. Please ensure the type is correct and the correct version of extension bundle is configured.");
//        }
//    }
//}
