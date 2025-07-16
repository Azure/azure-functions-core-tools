// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodePackTests : IClassFixture<NodeV4FunctionAppFixture>
    {
        private readonly NodeV4FunctionAppFixture _fixture;

        public NodePackTests(NodeV4FunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Pack_Node_WorksAsExpected()
        {
            var workingDir = _fixture.WorkingDirectory;
            var testName = nameof(Pack_Node_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                workingDir,
                testName,
                _fixture,
                new[]
                {
                    "host.json",
                    "package.json",
                    "src\\functions\\HttpTrigger.js",
                    "package-lock.json"
                });
        }
    }
}
