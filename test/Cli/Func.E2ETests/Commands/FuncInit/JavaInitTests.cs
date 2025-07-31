// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Java)]
    public class JavaInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("")]
        [InlineData("v1")]
        public void Init_With_SupportedModel_GeneratesExpectedFunctionProjectFiles(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var programmingModelFlag = string.IsNullOrEmpty(programmingModel) ? string.Empty : $"--model {programmingModel}";
            var testName = nameof(Init_With_SupportedModel_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "java" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize java function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "java", programmingModel]);

            // Validate expected output content
            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("v2")]
        public void Init_With_UnsupportedModel_DisplaysError(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_With_UnsupportedModel_DisplaysError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize java function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "java", "--model", programmingModel]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"programming model is not supported for worker runtime java.");
        }
    }
 }
