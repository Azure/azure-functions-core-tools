// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    public class DotnetInProcTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
        public async Task FuncNew_CreatesHttpTrigger_DotNetInProc()
        {
            var uniqueTestName = nameof(FuncNew_CreatesHttpTrigger_DotNetInProc);
            var workingDir = WorkingDirectory;

            // Initialize the function app
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet" });

            // Run func new
            var args = new[] { ".", "--template", "HttpTrigger", "--name", "HttpDotTriggFunc" };
            var result = await FuncNewWithResultRetryAsync(uniqueTestName, args, "dotnet");

            // Validate result
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"HttpDotTriggFunc\" was created successfully");
        }

        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
        public async Task FuncNew_CreatesHttpTrigger_AuthConfigured_Dotnet()
        {
            var uniqueTestName = nameof(FuncNew_CreatesHttpTrigger_AuthConfigured_Dotnet);
            var workingDir = WorkingDirectory;

            // Initialize the function app
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet" });

            // Run func new
            var args = new[] { ".", "--template", "HttpTrigger", "--name", "HttpAuthTriggFunc", "--authlevel", "function" };
            var result = await FuncNewWithResultRetryAsync(uniqueTestName, args, "dotnet");

            // Validate result
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"HttpAuthTriggFunc\" was created successfully");
        }

        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
        public void FuncNew_HttpTrigger_CsxMode_WithoutInit_Succeeds()
        {
            var testName = nameof(FuncNew_HttpTrigger_CsxMode_WithoutInit_Succeeds);

            // Create a CSX-based function without calling init
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--csx", "--template", "HttpTrigger", "--name", "testfunc"]);

            // Validate command success and output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"HttpTrigger\" template.");

            // Verify function.json contains httpTrigger
            var functionJsonPath = Path.Combine(WorkingDirectory, "testfunc", "function.json");
            var functionJson = File.ReadAllText(functionJsonPath);
            functionJson.Should().Contain("httpTrigger");
        }
    }
}
