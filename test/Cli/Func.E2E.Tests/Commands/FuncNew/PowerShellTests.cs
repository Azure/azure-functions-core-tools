// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    public class PowerShellTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Powershell)]
        public void FuncNew_HttpTrigger_AuthLevelConfigured_PowerShell_Succeeds()
        {
            var testName = nameof(FuncNew_HttpTrigger_AuthLevelConfigured_PowerShell_Succeeds);

            // Initialize PowerShell project
            new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--worker-runtime", "powershell"]);

            // Create HTTP Trigger function with anonymous authlevel
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--template", "HttpTrigger", "--name", "MyHttpTriggerFunction", "--authlevel", "Anonymous", "-a"]);

            // Verify output contains success message
            result.Should().HaveStdOutContaining("The function \"MyHttpTriggerFunction\" was created successfully from the \"HttpTrigger\" template.");

            // Verify generated function.json and authLevel
            var functionJsonPath = Path.Combine(WorkingDirectory, "MyHttpTriggerFunction", "function.json");
            var functionJsonContent = File.ReadAllText(functionJsonPath);
            functionJsonContent.Should().Contain("\"authLevel\": \"anonymous\"");
            functionJsonContent.Should().Contain("\"type\": \"httpTrigger\"");
        }
    }
}
