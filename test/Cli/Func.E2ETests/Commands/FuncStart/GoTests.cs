// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Go)]
    public class GoTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Start_Go_ExecutesHttpFunction(bool noBuild)
        {
            var testName = $"{nameof(Start_Go_ExecutesHttpFunction)}_{noBuild}";
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "go"]);

            var binaryPath = Path.Combine(WorkingDirectory, GoHelpers.LocalBinaryPath);
            if (noBuild)
            {
                await GoHelpers.BuildProject(WorkingDirectory);
                File.Exists(binaryPath).Should().BeTrue();
            }
            else
            {
                File.Exists(binaryPath).Should().BeFalse();
            }

            var port = ProcessHelper.GetAvailablePort();
            var command = new FuncStartCommand(FuncPath, testName, Log);
            string? response = null;
            command.ProcessStartedHandler = async process =>
            {
                response = await ProcessHelper.ProcessStartedHandlerHelper(
                    port,
                    process,
                    command.FileWriter ?? throw new ArgumentNullException(nameof(command.FileWriter)),
                    "hello");
            };

            var arguments = new List<string> { "--port", port.ToString() };
            if (noBuild)
            {
                arguments.Add("--no-build");
            }

            var result = command
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "native")
                .WithEnvironmentVariable(Common.Constants.FunctionsCliNativeLanguage, "go")
                .Execute(arguments);

            response.Should().Be("Hello from Go Worker!");
            File.Exists(binaryPath).Should().BeTrue();
            if (noBuild)
            {
                result.Should().NotHaveStdOutContaining("Building Go worker binary");
            }
            else
            {
                result.Should().HaveStdOutContaining($"Building Go worker binary '{GoHelpers.LocalBinaryPath}'");
            }
        }
    }
}
