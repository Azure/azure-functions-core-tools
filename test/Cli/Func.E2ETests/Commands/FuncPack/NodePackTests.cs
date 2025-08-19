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

            await NpmHelper.RunNpmCommand($"run build");

            var result = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(NodeProjectPath)
                .Execute(new[] { "--no-build", "-o", "nodecustomzip" });

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Skipping build event for functions project (--no-build).");
            result.Should().NotHaveStdOutContaining("Running 'npm install'...");

            var expectedZip = Path.Combine(NodeProjectPath, "nodecustomzip.zip");
            File.Exists(expectedZip).Should().BeTrue();

            result.Should().ValidateZipContents(
                expectedZip,
                new[]
                {
                    "host.json",
                    "package.json",
                    Path.Combine("src", "functions", "HttpTrigger.js"),
                    Path.Combine("node_modules", ".package-lock.json")
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

            var expectedZip = Path.Combine(NodeProjectPath, customOutput + ".zip");
            File.Exists(expectedZip).Should().BeTrue();

            result.Should().ValidateZipContents(
                expectedZip,
                new[]
                {
                    "host.json",
                    "package.json",
                    Path.Combine("src", "functions", "HttpTrigger.js")
                },
                Log);

            File.Delete(expectedZip);
        }
    }
}
