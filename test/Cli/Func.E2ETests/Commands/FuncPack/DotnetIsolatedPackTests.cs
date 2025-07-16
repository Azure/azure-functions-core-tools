// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Fixtures;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class DotnetIsolatedPackTests : IClassFixture<DotnetIsolatedFunctionAppFixture>
    {
        private readonly DotnetIsolatedFunctionAppFixture _fixture;

        public DotnetIsolatedPackTests(DotnetIsolatedFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Pack_DotnetIsolated_WorksAsExpected()
        {
            var workingDir = _fixture.WorkingDirectory;
            var testName = nameof(Pack_DotnetIsolated_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                workingDir,
                testName,
                _fixture,
                new[]
                {
                    "host.json",
                    "HttpTrigger.cs",
                    "Properties\\launchSettings.json",
                    "Program.cs",
                    "obj\\project.assets.json",
                    "obj\\project.nuget.cache"
                });
        }
    }
}
