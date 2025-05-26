// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.E2E.Tests.Fixtures;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew.TestsWithFixtures
{
    //public class DotnetIsolatedTests : IClassFixture<DotnetIsolatedFunctionAppFixture>
    //{
    //    private readonly DotnetIsolatedFunctionAppFixture _fixture;

    //    public DotnetIsolatedTests(DotnetIsolatedFunctionAppFixture fixture)
    //    {
    //        _fixture = fixture;
    //    }

    //    [Fact]
    //    public void FuncNew_HttpTrigger_CreatesFunction()
    //    {
    //        var testName = nameof(FuncNew_HttpTrigger_CreatesFunction);
    //        var newCommand = new FuncNewCommand(_fixture.FuncPath, testName, _fixture.Log);

    //        var result = newCommand
    //            .WithWorkingDirectory(_fixture.WorkingDirectory)
    //            .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated")
    //            .Execute([".", "--template", "HttpTrigger", "--name", "MyHttpFunction"]);

    //        result.Should().HaveStdOutContaining("The function \"MyHttpFunction\" was created successfully");
    //    }
    //}
}
