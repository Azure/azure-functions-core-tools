// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class DotnetIsolatedPackTests : BaseE2ETests
    {
        public DotnetIsolatedPackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string DotnetIsolatedProjectPath => Path.Combine(TestProjectDirectory, "TestDotnet8IsolatedProject");

        [Fact]
        public void Pack_DotnetIsolated_WorksAsExpected()
        {
            var testName = nameof(Pack_DotnetIsolated_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                DotnetIsolatedProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "TestDotnet8IsolatedProject.csproj",
                    "Program.cs",
                    "Function1.cs"
                });
        }

        [Fact]
        public void Pack_DotnetIsolated_WithBuildLocal_WorksAsExpected()
        {
            var testName = nameof(Pack_DotnetIsolated_WithBuildLocal_WorksAsExpected);

            BasePackTests.TestBuildLocalFlag(
                DotnetIsolatedProjectPath,
                testName,
                FuncPath,
                Log,
                false);
        }
    }
}
