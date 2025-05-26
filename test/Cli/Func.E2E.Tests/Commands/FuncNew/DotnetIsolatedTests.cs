// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class DotnetIsolatedTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        //[Fact]
        //public async Task FuncNew_CreatesHttpTrigger_DotNetIsolated()
        //{
        //    var uniqueTestName = nameof(FuncNew_CreatesHttpTrigger_DotNetIsolated);
        //    var funcNewCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log ?? throw new ArgumentNullException(nameof(Log)));
        //    var workingDir = WorkingDirectory;

        //    // Initialize the function app
        //    await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet-isolated" });

        //    // Run func new
        //    var funcNewResult = funcNewCommand
        //        .WithWorkingDirectory(workingDir)
        //        .Execute([".", "--template", "HttpTrigger", "--name", "HttpFunction"]);

        //    // Validate result
        //    funcNewResult.Should().HaveStdOutContaining("The function \"HttpFunction\" was created successfully");
        //}

        //[Fact]
        //public async Task FuncNew_CreatesTimmerTrigger_DotNetIsolated()
        //{
        //    var uniqueTestName = nameof(FuncNew_CreatesTimmerTrigger_DotNetIsolated);
        //    var funcNewCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log ?? throw new ArgumentNullException(nameof(Log)));
        //    var workingDir = WorkingDirectory;

        //    // Initialize the function app
        //    await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet-isolated" });

        //    // Create func new command using consistent working directory
        //    var funcNewResult = funcNewCommand
        //        .WithWorkingDirectory(workingDir)
        //        .Execute([".", "--template", "TimerTrigger", "--name", "TimerFunction"]);

        //    // Validate result
        //    funcNewResult.Should().HaveStdOutContaining("The function \"TimerFunction\" was created successfully");
        //}
    }
}
