// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc6PackTests : IClassFixture<Dotnet6InProcFunctionAppFixture>
    {
        private readonly Dotnet6InProcFunctionAppFixture _fixture;

        public DotnetInProc6PackTests(Dotnet6InProcFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Pack_Dotnet6InProc_WorksAsExpected()
        {
            var workingDir = _fixture.WorkingDirectory;
            var testName = nameof(Pack_Dotnet6InProc_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                workingDir,
                testName,
                _fixture,
                new[]
                {
                    "host.json",
                    "HttpTrigger.cs",
                    "Properties\\launchSettings.json"
                });
        }
    }
}
