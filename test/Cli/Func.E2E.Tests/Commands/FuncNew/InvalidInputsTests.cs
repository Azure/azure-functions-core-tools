// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    public class InvalidInputsTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void FuncNew_InvalidTemplate_ShowsError()
        {
            var testName = nameof(FuncNew_InvalidTemplate_ShowsError);

            // Init a function app with node
            new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--worker-runtime", "node"]);

            // Try creating a function with an invalid template
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--template", "invalidTemplate", "--name", "testfunc"]);

            result.Should().HaveStdErrContaining("Can't find template \"invalidTemplate\" in \"javascript\"");
        }

        [Fact]
        public void FuncNew_InvalidAuthLevel_ShowsError()
        {
            var testName = nameof(FuncNew_InvalidAuthLevel_ShowsError);

            // Init with node
            new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--worker-runtime", "node"]);

            // Try creating function with invalid auth level
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--template", "httpTrigger", "--name", "testfunc", "--authlevel", "superadmin"]);

            result.Should().HaveStdOutContaining("Authorization level is applicable to templates that use Http trigger, Allowed values: [function, anonymous, admin]");
        }
    }
}
