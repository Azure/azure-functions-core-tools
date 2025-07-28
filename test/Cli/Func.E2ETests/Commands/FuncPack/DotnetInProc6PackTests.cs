// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc6PackTests : BaseE2ETests
    {
        public DotnetInProc6PackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string Dotnet6ProjectPath => Path.Combine(TestProjectDirectory, "TestNet6InProcProject");

        [Fact]
        public void Pack_Dotnet6InProc_WorksAsExpected()
        {
            var testName = nameof(Pack_Dotnet6InProc_WorksAsExpected);
            Log.WriteLine(Dotnet6ProjectPath);

            BasePackTests.TestBasicPackFunctionality(
                Dotnet6ProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "Dotnet6InProc.cs",
                    "TestNet6InProcProject.csproj"
                });
        }

        [Fact]
        public void Pack_Dotnet6InProc_WithBuildLocal_ShouldFail()
        {
            var testName = nameof(Pack_Dotnet6InProc_WithBuildLocal_ShouldFail);

            BasePackTests.TestBuildLocalFlagForNonPythonRuntime(
                Dotnet6ProjectPath,
                testName,
                FuncPath,
                Log,
                "dotnet");
        }
    }
}
