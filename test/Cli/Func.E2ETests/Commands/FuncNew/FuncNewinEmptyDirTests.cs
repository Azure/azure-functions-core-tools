// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncNew
{
    public class FuncNewinEmptyDirTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        public static IEnumerable<object[]> WorkerRuntimeTemplateMatrix =>
            new List<object[]>
            {
                new object[] { "dotnet", "HttpTrigger" },
                new object[] { "dotnet-isolated", "HttpTrigger" },
                new object[] { "node", "HttpTrigger" },
                new object[] { "node", "TypeScript" },
                new object[] { "python", "\"HTTP Trigger\"" },
                new object[] { "powershell", "HttpTrigger" },
                new object[] { "custom", "HttpTrigger" }
            };

        [Theory]
        [MemberData(nameof(WorkerRuntimeTemplateMatrix))]
        public void FuncInit_Then_FuncNew_WithTemplateAndName_ShouldCreateFunctionSuccessfully_ForAllWorkerRuntimes(string workerRuntime, string template)
        {
            var testName = $"FuncInit_Then_FuncNew_{workerRuntime}_ShouldCreateFunctionSuccessfully";
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            try
            {
                var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log);
                var initArgs = new List<string> { ".", "--worker-runtime", workerRuntime };
                if (workerRuntime == "node" && template == "TypeScript")
                {
                    initArgs.Add("--language");
                    initArgs.Add("typescript");
                }

                var initResult = funcInitCommand
                    .WithWorkingDirectory(tempDir)
                    .Execute(initArgs);
                initResult.Should().ExitWith(0);

                var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
                var funcName = $"TestFunction_{workerRuntime}";
                var templateArg = (workerRuntime == "python") ? "\"HTTP Trigger\"" : (template == "TypeScript" ? "HttpTrigger" : template);
                var args = new List<string> { ".", "--template", templateArg, "--name", funcName };

                if (workerRuntime == "python" && template == "\"HTTP Trigger\"")
                {
                    args.Add("--authlevel");
                    args.Add("anonymous");
                }

                var newResult = funcNewCommand
                    .WithWorkingDirectory(tempDir)
                    .Execute(args);

                newResult.Should().ExitWith(0);
                newResult.Should().HaveStdOutContaining($"The function \"{funcName}\" was created successfully");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Theory]
        [MemberData(nameof(WorkerRuntimeTemplateMatrix))]
        public void FuncNew_In_EmptyDirectory_WithTemplateAndName_ShouldCreateFunctionSuccessfully_ForAllWorkerRuntimes(string workerRuntime, string template)
        {
            var testName = $"FuncNew_In_EmptyDirectory_{workerRuntime}_ShouldCreateFunctionSuccessfully";
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            try
            {
                var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
                var funcName = $"TestFunction_{workerRuntime}";
                var templateArg = (workerRuntime == "python") ? "\"HTTP Trigger\"" : (template == "TypeScript" ? "HttpTrigger" : template);
                var args = new List<string> { ".", "--worker-runtime", workerRuntime, "--template", templateArg, "--name", funcName };
                if (workerRuntime == "node" && template == "TypeScript")
                {
                    args.Add("--language");
                    args.Add("typescript");
                }

                if (workerRuntime == "python" && template == "\"HTTP Trigger\"")
                {
                    args.Add("--authlevel");
                    args.Add("anonymous");
                }

                var result = funcNewCommand
                    .WithWorkingDirectory(tempDir)
                    .Execute(args);

                result.Should().ExitWith(0);
                result.Should().HaveStdOutContaining($"The function \"{funcName}\" was created successfully");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
