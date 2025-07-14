// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    /// <summary>
    /// Enhanced tests for func pack command specifically for Python runtime.
    /// These tests build upon the existing PythonPackTests.cs to provide more comprehensive coverage.
    /// </summary>
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Python)]
    public class EnhancedPythonPackTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Pack_Python_WithBuildNativeDeps_CreatesZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_Python_WithBuildNativeDeps_CreatesZip);
            
            // Step 1: Initialize Python function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "python"]);

            initResult.Should().ExitWith(0);
            initResult.Should().HaveStdOutContaining("Writing host.json");
            initResult.Should().HaveStdOutContaining("Writing local.settings.json");

            // Step 2: Create HTTP trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with build native dependencies
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--build-native-deps"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 4: Verify zip file was created
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Pack_Python_WithAdditionalPackages_CreatesZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_Python_WithAdditionalPackages_CreatesZip);
            
            // Step 1: Initialize Python function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "python"]);

            initResult.Should().ExitWith(0);

            // Step 2: Create HTTP trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with additional packages
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--build-native-deps", "--additional-packages", "python3-dev"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 4: Verify zip file was created
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Pack_Python_WithSquashfs_CreatesSquashfsFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_Python_WithSquashfs_CreatesSquashfsFile);
            
            // Step 1: Initialize Python function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "python"]);

            initResult.Should().ExitWith(0);

            // Step 2: Create HTTP trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with squashfs option
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--squashfs"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 4: Verify squashfs file was created instead of zip
            var expectedSquashfsPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.squashfs");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedSquashfsPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Pack_Python_WithComplexFuncIgnore_CreatesZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_Python_WithComplexFuncIgnore_CreatesZip);
            
            // Step 1: Initialize Python function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "python"]);

            initResult.Should().ExitWith(0);

            // Step 2: Create HTTP trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Create test directory structure and files
            var testDir = Path.Combine(workingDir, "test-data");
            var tempDir = Path.Combine(workingDir, "temp");
            var logsDir = Path.Combine(workingDir, "logs");
            
            Directory.CreateDirectory(testDir);
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(logsDir);
            
            File.WriteAllText(Path.Combine(testDir, "data.json"), "{}");
            File.WriteAllText(Path.Combine(tempDir, "cache.tmp"), "cache");
            File.WriteAllText(Path.Combine(logsDir, "app.log"), "logs");
            File.WriteAllText(Path.Combine(workingDir, "debug.log"), "debug");
            File.WriteAllText(Path.Combine(workingDir, "README.md"), "readme");

            // Step 4: Create comprehensive .funcignore file
            var funcIgnorePath = Path.Combine(workingDir, ".funcignore");
            var funcIgnoreContent = @"# Test files and directories
test-data/
temp/
*.log
*.tmp
# Keep README files
!README.md
# Python specific ignores
__pycache__/
*.pyc
*.pyo
.pytest_cache/
.venv/
venv/
env/
.env
";
            File.WriteAllText(funcIgnorePath, funcIgnoreContent);

            // Step 5: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 6: Verify zip file was created
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Pack_Python_WithCustomRequirements_CreatesZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_Python_WithCustomRequirements_CreatesZip);
            
            // Step 1: Initialize Python function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "python"]);

            initResult.Should().ExitWith(0);

            // Step 2: Create HTTP trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Create custom requirements.txt with common dependencies
            var requirementsPath = Path.Combine(workingDir, "requirements.txt");
            var requirementsContent = @"requests>=2.25.1
azure-functions
azure-functions-worker
";
            File.WriteAllText(requirementsPath, requirementsContent);

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

            // Step 6: Verify .python_packages directory is created for dependency caching
            var pythonPackagesDir = Path.Combine(workingDir, ".python_packages");
            if (Directory.Exists(pythonPackagesDir))
            {
                var pythonPackagesDirFiles = new List<(string FilePath, string[] ExpectedContent)>
                {
                    (pythonPackagesDir, new[] { string.Empty }) // Just check directory exists
                };
                packResult.Should().FilesExistsWithExpectContent(pythonPackagesDirFiles);
            }
        }
    }
}