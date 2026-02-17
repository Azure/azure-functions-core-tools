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
    public class FuncIgnorePackTests : BaseE2ETests
    {
        public FuncIgnorePackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string NodeProjectPath => Path.Combine(TestProjectDirectory, "TestNodeProject");

        [Fact]
        public void Pack_Node_FuncIgnore_ExcludesMatchedFiles()
        {
            var testName = nameof(Pack_Node_FuncIgnore_ExcludesMatchedFiles);

            // Create a file that should be excluded by .funcignore
            var readmePath = Path.Combine(NodeProjectPath, "README.md");
            var testDocPath = Path.Combine(NodeProjectPath, "CONTRIBUTING.md");
            File.WriteAllText(readmePath, "# Test README");
            File.WriteAllText(testDocPath, "# Test CONTRIBUTING");

            try
            {
                // .funcignore that excludes *.md files
                var funcIgnoreContent = "*.js.map\n*.ts\n.git*\n.vscode\nlocal.settings.json\n*.md\n";

                BasePackTests.TestFuncIgnoreExcludesFiles(
                    NodeProjectPath,
                    testName,
                    FuncPath,
                    Log,
                    filesToValidatePresent: new[]
                    {
                        "host.json",
                        "package.json",
                    },
                    filesToValidateAbsent: new[]
                    {
                        "README.md",
                        "CONTRIBUTING.md",
                        "local.settings.json",
                    },
                    funcIgnoreContent: funcIgnoreContent,
                    additionalPackArgs: new[] { "--no-build" });
            }
            finally
            {
                // Clean up test files
                if (File.Exists(readmePath))
                {
                    File.Delete(readmePath);
                }

                if (File.Exists(testDocPath))
                {
                    File.Delete(testDocPath);
                }
            }
        }

        [Fact]
        public void Pack_Node_NoFuncIgnore_IncludesAllFiles()
        {
            var testName = nameof(Pack_Node_NoFuncIgnore_IncludesAllFiles);

            // Create a test file that would normally be excluded
            var readmePath = Path.Combine(NodeProjectPath, "README.md");
            File.WriteAllText(readmePath, "# Test README");

            // Temporarily remove .funcignore
            var funcIgnorePath = Path.Combine(NodeProjectPath, ".funcignore");
            string? originalContent = null;
            if (File.Exists(funcIgnorePath))
            {
                originalContent = File.ReadAllText(funcIgnorePath);
                File.Delete(funcIgnorePath);
            }

            try
            {
                var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
                var packResult = funcPackCommand
                    .WithWorkingDirectory(NodeProjectPath)
                    .Execute(new[] { "--no-build" });

                packResult.Should().ExitWith(0);

                var zipFiles = Directory.GetFiles(NodeProjectPath, "*.zip");
                Assert.True(zipFiles.Length > 0, $"No zip files found in {NodeProjectPath}");

                // Without .funcignore, README.md should be included
                packResult.Should().ValidateZipContents(
                    zipFiles.First(),
                    new[]
                    {
                        "host.json",
                        "package.json",
                        "README.md",
                    },
                    Log);

                File.Delete(zipFiles.First());
            }
            finally
            {
                // Restore .funcignore
                if (originalContent != null)
                {
                    File.WriteAllText(funcIgnorePath, originalContent);
                }

                if (File.Exists(readmePath))
                {
                    File.Delete(readmePath);
                }
            }
        }

        [Fact]
        public void Pack_Node_FuncIgnore_NegationPattern_IncludesFile()
        {
            var testName = nameof(Pack_Node_FuncIgnore_NegationPattern_IncludesFile);

            // Create test files
            var readmePath = Path.Combine(NodeProjectPath, "README.md");
            var licensePath = Path.Combine(NodeProjectPath, "LICENSE.md");
            File.WriteAllText(readmePath, "# Test README");
            File.WriteAllText(licensePath, "# License");

            try
            {
                // Exclude all *.md, but negate (re-include) LICENSE.md
                var funcIgnoreContent = "*.js.map\n*.ts\n.git*\n.vscode\nlocal.settings.json\n*.md\n!LICENSE.md\n";

                BasePackTests.TestFuncIgnoreExcludesFiles(
                    NodeProjectPath,
                    testName,
                    FuncPath,
                    Log,
                    filesToValidatePresent: new[]
                    {
                        "host.json",
                        "package.json",
                        "LICENSE.md",
                    },
                    filesToValidateAbsent: new[]
                    {
                        "README.md",
                    },
                    funcIgnoreContent: funcIgnoreContent,
                    additionalPackArgs: new[] { "--no-build" });
            }
            finally
            {
                if (File.Exists(readmePath))
                {
                    File.Delete(readmePath);
                }

                if (File.Exists(licensePath))
                {
                    File.Delete(licensePath);
                }
            }
        }
    }
}
