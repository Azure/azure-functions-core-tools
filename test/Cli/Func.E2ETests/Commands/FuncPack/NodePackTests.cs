// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodePackTests : BaseE2ETests
    {
        public NodePackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string NodeProjectPath => Path.Combine(TestProjectDirectory, "TestNodeProject");

        [Fact]
        public void Pack_Node_WorksAsExpected()
        {
            var testName = nameof(Pack_Node_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                NodeProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "package.json",
                    Path.Combine("src", "functions", "HttpTrigger.js"),
                    "package-lock.json"
                });
        }

        [Fact]
        public void Pack_Node_WithBuildLocal_ShouldFail()
        {
            var testName = nameof(Pack_Node_WithBuildLocal_ShouldFail);

            BasePackTests.TestBuildLocalFlagForNonPythonRuntime(
                NodeProjectPath,
                testName,
                FuncPath,
                Log,
                "node");
        }
    }
}
