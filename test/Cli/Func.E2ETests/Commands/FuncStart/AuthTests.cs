// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
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
    /// Tests for func start authentication features using pre-built test app.
    /// </summary>
    public class AuthTests(ITestOutputHelper log, PreBuiltDotnetIsolatedFixture fixture)
        : IClassFixture<PreBuiltDotnetIsolatedFixture>
    {
        private readonly ITestOutputHelper _log = log;
        private readonly PreBuiltDotnetIsolatedFixture _fixture = fixture;

        [Theory]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        [InlineData("function", false, "Welcome to Azure Functions!")]
        [InlineData("function", true, "")]
        [InlineData("anonymous", true, "Welcome to Azure Functions!")]
        public async Task Start_DotnetIsolated_EnableAuthFeature(
            string authLevel,
            bool enableAuth,
            string expectedResult)
        {
            var port = ProcessHelper.GetAvailablePort();

            var methodName = nameof(Start_DotnetIsolated_EnableAuthFeature);
            var uniqueTestName = $"{methodName}_{authLevel}_{enableAuth}";

            // Verify fixture was initialized correctly
            if (!Directory.Exists(_fixture.WorkingDirectory) || !File.Exists(Path.Combine(_fixture.WorkingDirectory, "host.json")))
            {
                throw new InvalidOperationException($"Fixture working directory is not set up correctly. WorkingDirectory: {_fixture.WorkingDirectory}, Exists: {Directory.Exists(_fixture.WorkingDirectory)}");
            }

            // Create a unique subdirectory for this test to avoid conflicts
            var workingDir = Path.Combine(Path.GetTempPath(), $"auth_test_{Guid.NewGuid():N}");
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, workingDir);

            try
            {
                // Only need func new to set the auth level (pre-built app already has HttpTrigger)
                // For auth level tests, we need to create a new function with the specific auth level
                await FunctionAppSetupHelper.FuncNewWithRetryAsync(
                    _fixture.FuncPath,
                    uniqueTestName,
                    workingDir,
                    _log,
                    [".", "--template", "Httptrigger", "--name", "HttpTrigger", "--authlevel", authLevel, "--force"]);

                // Build the project after modifying the function to ensure the new auth level is compiled
                var buildProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "build --configuration Release",
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    }
                };
                buildProcess.Start();
                await buildProcess.WaitForExitAsync();

                if (buildProcess.ExitCode != 0)
                {
                    var error = await buildProcess.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"Failed to build project. Exit code: {buildProcess.ExitCode}. Error: {error}");
                }

                // Call func start
                var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, methodName, _log);

                string? capturedContent = null;
                HttpStatusCode? capturedStatusCode = null;

                funcStartCommand.ProcessStartedHandler = async (process) =>
                {
                    var (content, statusCode) = await ProcessHelper.ProcessStartedHandlerHelperWithStatusCode(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand)), "HttpTrigger");
                    capturedContent = content;
                    capturedStatusCode = statusCode;
                };

                // Build command arguments based on enableAuth parameter
                var commandArgs = new List<string> { "--verbose", "--port", port.ToString() };
                if (enableAuth)
                {
                    commandArgs.Add("--enableAuth");
                }

                var result = funcStartCommand
                    .WithWorkingDirectory(workingDir)
                    .Execute([.. commandArgs]);

                // Validate expected output content
                if (string.IsNullOrEmpty(expectedResult))
                {
                    // When auth is enabled and authLevel is "function", calling without a key should return 401
                    capturedStatusCode.Should().Be(HttpStatusCode.Unauthorized, "calling a function-level auth endpoint without a key should return 401 Unauthorized");
                }
                else
                {
                    capturedContent.Should().Be(expectedResult);
                    result.Should().StartOutOfProcessHost();
                }
            }
            finally
            {
                // Cleanup test directory
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
