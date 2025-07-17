// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Powershell)]
    public class PowershellPackTests : BaseE2ETests
    {
        public PowershellPackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string PowershellProjectPath => Path.Combine(TestProjectDirectory, "TestPowershellProject");

        [Fact]
        public void Pack_Powershell_WorksAsExpected()
        {
            var testName = nameof(Pack_Powershell_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                PowershellProjectPath,
                testName,
                FuncPath,
                log,
                new[]
                {
                    "host.json",
                    "requirements.psd1",
                    "HttpTrigger\\run.ps1",
                    "profile.ps1",
                    "HttpTrigger\\function.json"
                });
        }
    }
}
