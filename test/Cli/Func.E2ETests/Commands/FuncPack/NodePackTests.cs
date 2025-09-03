// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
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

            var logsToValidate = new[]
            {
                "Running 'npm install'...",
                "Running 'npm run build'...",
            };

            if (Directory.Exists(Path.Combine(NodeProjectPath, "node_modules")))
            {
                Directory.Delete(Path.Combine(NodeProjectPath, "node_modules"), true);
            }

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
                    "package-lock.json",
                    Path.Combine("node_modules", ".package-lock.json")
                },
                logsToValidate);
        }

        [Fact]
        public async Task Pack_Node_CustomOutput_NoBuild()
        {
            var testName = nameof(Pack_Node_CustomOutput_NoBuild);

            if (!Directory.Exists(Path.Combine(NodeProjectPath, "node_modules")))
            {
                Environment.CurrentDirectory = NodeProjectPath;
                await NpmHelper.RunNpmCommand($"install");
            }

            // Run npm run build first
            await NpmHelper.RunNpmCommand($"run build");

            // Now pack from the directory produced by npm build (nodecustom)
            var result = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(NodeProjectPath)
                .Execute(new[] { "--no-build", "-o", "nodecustomzip" });

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Skipping build event for functions project (--no-build).");
            result.Should().NotHaveStdOutContaining("Running 'npm install'...");

            var expectedZip = Path.Combine(NodeProjectPath, "nodecustomzip", "TestNodeProject.zip");
            File.Exists(expectedZip).Should().BeTrue();

            result.Should().ValidateZipContents(
                expectedZip,
                new[]
                {
                    "host.json",
                    "package.json",
                    Path.Combine("src", "functions", "HttpTrigger.js"),
                    "package-lock.json"
                },
                Log);

            File.Delete(expectedZip);
        }

        [Fact]
        public async Task Pack_Node_SkipInstall_CreatesPackage()
        {
            var testName = nameof(Pack_Node_SkipInstall_CreatesPackage);
            var customOutput = "nodeskipinstall";

            if (!Directory.Exists(Path.Combine(NodeProjectPath, "node_modules")))
            {
                Environment.CurrentDirectory = NodeProjectPath;
                await NpmHelper.Install();
            }

            var result = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(NodeProjectPath)
                .Execute(new[] { "--skip-install", "-o", customOutput });

            result.Should().ExitWith(0);
            result.Should().NotHaveStdOutContaining("Running 'npm install'...");
            result.Should().NotHaveStdOutContaining("Skipping build event for functions project (--no-build).");
            result.Should().HaveStdOutContaining("Running 'npm run build'...");

            var expectedZip = Path.Combine(NodeProjectPath, customOutput, "TestNodeProject.zip");
            File.Exists(expectedZip).Should().BeTrue();

            result.Should().ValidateZipContents(
                expectedZip,
                new[]
                {
                    "host.json",
                    "package.json",
                    Path.Combine("src", "functions", "HttpTrigger.js"),
                    "package-lock.json",
                    Path.Combine("node_modules", ".package-lock.json")
                },
                Log);

            File.Delete(expectedZip);
        }

        [Fact]
        public void Pack_Node_WithRelativePathArgument_Works()
        {
            var testName = nameof(Pack_Node_WithRelativePathArgument_Works);
            var projectName = "TestNodeProject";
            BasePackTests.TestPackWithPathArgument(
                funcInvocationWorkingDir: TestProjectDirectory,
                projectAbsoluteDir: Path.Combine(TestProjectDirectory, projectName),
                pathArgumentToPass: $"./{projectName}",
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    "package.json",
                    Path.Combine("src", "functions", "HttpTrigger.js"),
                    "package-lock.json",
                    Path.Combine("node_modules", ".package-lock.json")
                });
        }

        [Fact]
        public void Pack_Node_WithAbsolutePathArgument_Works()
        {
            var testName = nameof(Pack_Node_WithAbsolutePathArgument_Works);
            var projectAbs = NodeProjectPath;
            BasePackTests.TestPackWithPathArgument(
                funcInvocationWorkingDir: WorkingDirectory,
                projectAbsoluteDir: projectAbs,
                pathArgumentToPass: projectAbs,
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    "package.json",
                    Path.Combine("src", "functions", "HttpTrigger.js"),
                    "package-lock.json",
                    Path.Combine("node_modules", ".package-lock.json")
                });
        }
    }
}
