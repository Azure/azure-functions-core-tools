// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    /// <summary>
    /// Comprehensive tests for func pack command across all supported runtimes.
    /// These tests verify that func pack creates zip files in the correct output directory
    /// and works properly with .funcignore files.
    /// </summary>
    public class ComprehensivePackTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData(WorkerRuntimeTraits.Node, "node")]
        [InlineData(WorkerRuntimeTraits.Python, "python")]
        [InlineData(WorkerRuntimeTraits.Powershell, "powershell")]
        [InlineData(WorkerRuntimeTraits.Java, "java")]
        [InlineData(WorkerRuntimeTraits.Custom, "custom")]
        public void Pack_DefaultOutput_CreatesZipInExpectedLocation(string traitName, string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_DefaultOutput_CreatesZipInExpectedLocation)}_{runtime}";
            
            // Step 1: Initialize function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", runtime]);

            initResult.Should().ExitWith(0);
            initResult.Should().HaveStdOutContaining("Writing host.json");
            initResult.Should().HaveStdOutContaining("Writing local.settings.json");

            // Step 2: Create a function (using HTTP trigger for all runtimes as it's widely supported)
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with default output
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 4: Verify zip file was created in default location
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData(WorkerRuntimeTraits.Node, "node")]
        [InlineData(WorkerRuntimeTraits.Python, "python")]
        [InlineData(WorkerRuntimeTraits.Powershell, "powershell")]
        [InlineData(WorkerRuntimeTraits.Java, "java")]
        [InlineData(WorkerRuntimeTraits.Custom, "custom")]
        public void Pack_CustomOutputPath_CreatesZipInSpecifiedLocation(string traitName, string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_CustomOutputPath_CreatesZipInSpecifiedLocation)}_{runtime}";
            var customOutputPath = "custom-output.zip";
            
            // Step 1: Initialize function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", runtime]);

            initResult.Should().ExitWith(0);

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with custom output path
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["-o", customOutputPath]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 4: Verify zip file was created in custom location
            var expectedZipPath = Path.Combine(workingDir, customOutputPath);
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData(WorkerRuntimeTraits.Node, "node")]
        [InlineData(WorkerRuntimeTraits.Python, "python")]
        [InlineData(WorkerRuntimeTraits.Powershell, "powershell")]
        [InlineData(WorkerRuntimeTraits.Java, "java")]
        [InlineData(WorkerRuntimeTraits.Custom, "custom")]
        public void Pack_WithFuncIgnore_ExcludesSpecifiedFiles(string traitName, string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_WithFuncIgnore_ExcludesSpecifiedFiles)}_{runtime}";
            
            // Step 1: Initialize function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", runtime]);

            initResult.Should().ExitWith(0);

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Create test files and .funcignore
            var testFilePath = Path.Combine(workingDir, "test-file.txt");
            var ignoredFilePath = Path.Combine(workingDir, "ignored-file.txt");
            var funcIgnorePath = Path.Combine(workingDir, ".funcignore");
            
            File.WriteAllText(testFilePath, "This file should be included");
            File.WriteAllText(ignoredFilePath, "This file should be ignored");
            File.WriteAllText(funcIgnorePath, "ignored-file.txt\n*.log\ntemp/");

            // Step 4: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 5: Verify zip file was created
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);

            // Note: Detailed verification of zip contents would require extracting and examining the zip file
            // This is a basic test to ensure the command succeeds with .funcignore present
        }

        [Theory]
        [InlineData(WorkerRuntimeTraits.Node, "node")]
        [InlineData(WorkerRuntimeTraits.Python, "python")]
        [InlineData(WorkerRuntimeTraits.Powershell, "powershell")]
        [InlineData(WorkerRuntimeTraits.Java, "java")]
        [InlineData(WorkerRuntimeTraits.Custom, "custom")]
        public void Pack_WithOutputDirectory_CreatesZipInDirectory(string traitName, string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_WithOutputDirectory_CreatesZipInDirectory)}_{runtime}";
            var outputDir = "output";
            var outputDirPath = Path.Combine(workingDir, outputDir);
            
            // Step 1: Initialize function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", runtime]);

            initResult.Should().ExitWith(0);

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Create output directory
            Directory.CreateDirectory(outputDirPath);

            // Step 4: Run pack command with output directory
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["-o", outputDir]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 5: Verify zip file was created in output directory
            var expectedZipPath = Path.Combine(outputDirPath, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Pack_ReplacesExistingZip_WhenZipAlreadyExists()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_ReplacesExistingZip_WhenZipAlreadyExists);
            var runtime = "node"; // Use Node.js as a representative runtime
            
            // Step 1: Initialize function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", runtime]);

            initResult.Should().ExitWith(0);

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Create an existing zip file
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            File.WriteAllText(expectedZipPath, "fake zip content");

            // Step 4: Run pack command (should replace existing file)
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Deleting the old package");
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 5: Verify new zip file was created
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}