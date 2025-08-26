// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncNew.TestsWithFixtures
{
    [CollectionDefinition("Dotnet isolated func new tests", DisableParallelization = true)]
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class DotnetIsolatedTests : IClassFixture<DotnetIsolatedFunctionAppFixture>
    {
        private readonly DotnetIsolatedFunctionAppFixture _fixture;

        public DotnetIsolatedTests(DotnetIsolatedFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void FuncNew_HttpTrigger_CreatesFunction()
        {
            var testName = nameof(FuncNew_HttpTrigger_CreatesFunction);
            var newCommand = new FuncNewCommand(_fixture.FuncPath, testName, _fixture.Log);

            var result = newCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated")
                .Execute([".", "--template", "HttpTrigger", "--name", "MyHttpFunction"]);

            result.Should().HaveStdOutContaining("The function \"MyHttpFunction\" was created successfully");
        }
    }
}
