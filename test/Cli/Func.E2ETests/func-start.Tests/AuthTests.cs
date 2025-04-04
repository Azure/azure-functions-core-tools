using FluentAssertions;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using System.Net;
using Xunit.Abstractions;
using Xunit;
using Grpc.Net.Client.Configuration;
using System.Diagnostics;

namespace Func.E2ETests.func_start.Tests
{
    public class AuthTests : BaseE2ETest
    {
        public AuthTests(ITestOutputHelper log) : base(log)
        {
        }

        [Theory]
        [InlineData("function", false, "Welcome to Azure Functions!", "response from default function should be 'Welcome to Azure Functions!'")]
        [InlineData("function", true, "", "the call to the function is unauthorized")]
        [InlineData("anonymous", true, "Welcome to Azure Functions!", "response from default function should be 'Welcome to Azure Functions!'")]
        public async Task Start_DotnetIsolated_Test_EnableAuthFeature(
            string authLevel,
            bool enableAuth,
            string expectedResult,
            string becauseReason)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func init and func new
            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "dotnet-isolated" });
            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTrigger", "--authlevel", authLevel });

            string methodName = "Start_DotnetIsolated_Test_EnableAuthFeature";
            string uniqueTestName = $"{methodName}_{authLevel}_{enableAuth}";

            string capturedContent = null;

            string originalFuncDir = Path.GetDirectoryName(FuncPath);

            // Create a unique temporary directory
            string uniqueTempDir = Path.Combine(Path.GetTempPath(), $"func_copy_{Guid.NewGuid():N}");
            Directory.CreateDirectory(uniqueTempDir);

            // Copy all files from the original directory to the temp directory
            foreach (string file in Directory.GetFiles(originalFuncDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(originalFuncDir, file);
                string destFile = Path.Combine(uniqueTempDir, relativePath);

                // Ensure the destination directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(destFile));

                // Copy the file
                File.Copy(file, destFile);
            }

            // The path to the copied func.exe
            string uniqueFuncPath = Path.Combine(uniqueTempDir, Path.GetFileName(FuncPath));

            // Call func start
            var funcStartCommand = new FuncStartCommand(uniqueFuncPath, Log, methodName);
            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, fileWriter, "HttpTrigger");
            };

            // Build command arguments based on enableAuth parameter
            var commandArgs = new List<string> { "start", "--verbose", "--port", port.ToString() };
            if (enableAuth)
            {
                commandArgs.Add("--enableAuth");
            }

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(commandArgs.ToArray());

            // Validate expected output content
            if (string.IsNullOrEmpty(expectedResult))
            {
                result.Should().HaveStdOutContaining("\"status\": \"401\"");
            }
            else
            {
                result.Should().HaveStdOutContaining("Selected out-of-process host.");
            }
        }
    }
}
