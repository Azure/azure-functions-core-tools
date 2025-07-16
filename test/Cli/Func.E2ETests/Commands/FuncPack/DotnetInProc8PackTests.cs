// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc8PackTests : IClassFixture<Dotnet8InProcFunctionAppFixture>
    {
        private readonly Dotnet8InProcFunctionAppFixture _fixture;

        public DotnetInProc8PackTests(Dotnet8InProcFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Pack_Dotnet8InProc_WorksAsExpected()
        {
            var workingDir = _fixture.WorkingDirectory;
            var testName = nameof(Pack_Dotnet8InProc_WorksAsExpected);

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
