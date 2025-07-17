// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc8PackTests : BaseE2ETests
    {
        public DotnetInProc8PackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string Dotnet8ProjectPath => Path.Combine(TestProjectDirectory, "TestNet8InProcProject");

        [Fact]
        public void Pack_Dotnet8InProc_WorksAsExpected()
        {
            var testName = nameof(Pack_Dotnet8InProc_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                Dotnet8ProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "Dotnet8InProc.cs",
                    "TestNet8InProcProject.csproj"
                });
        }
    }
}
