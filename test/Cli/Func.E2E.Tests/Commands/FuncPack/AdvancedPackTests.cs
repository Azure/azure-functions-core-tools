// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    /// <summary>
    /// Advanced tests for func pack command that validate zip contents and test edge cases.
    /// These tests provide deeper validation of the pack functionality.
    /// </summary>
    public class AdvancedPackTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData(WorkerRuntimeTraits.Node, "node")]
        [InlineData(WorkerRuntimeTraits.Python, "python")]
        [InlineData(WorkerRuntimeTraits.Powershell, "powershell")]
        [InlineData(WorkerRuntimeTraits.Custom, "custom")]
        public void Pack_ValidatesZipContents_ContainsExpectedFiles(string traitName, string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_ValidatesZipContents_ContainsExpectedFiles)}_{runtime}";
            
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

            // Step 3: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);

            // Step 4: Verify zip file was created and validate contents
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            Assert.True(File.Exists(expectedZipPath), $"Zip file should exist at {expectedZipPath}");

            // Extract and validate zip contents
            using var archive = ZipFile.OpenRead(expectedZipPath);
            var entries = archive.Entries.Select(e => e.FullName).ToList();
            
            // All function apps should contain host.json and local.settings.json should be excluded
            Assert.Contains("host.json", entries);
            Assert.DoesNotContain("local.settings.json", entries);
            
            // Should contain the function folder
            Assert.Contains(entries, entry => entry.StartsWith("httptrigger/"));
            
            Log.WriteLine($"Zip contains {entries.Count} entries:");
            foreach (var entry in entries.Take(10)) // Log first 10 entries for debugging
            {
                Log.WriteLine($"  - {entry}");
            }
        }

        [Fact]
        public void Pack_WithFuncIgnore_ExcludesFilesFromZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_WithFuncIgnore_ExcludesFilesFromZip);
            var runtime = "node"; // Use Node.js for this test
            
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

            // Step 3: Create test files and directories
            var testFile = Path.Combine(workingDir, "test-file.txt");
            var ignoredFile = Path.Combine(workingDir, "ignored-file.txt");
            var testDir = Path.Combine(workingDir, "test-dir");
            var ignoredDir = Path.Combine(workingDir, "ignored-dir");
            
            File.WriteAllText(testFile, "This should be included");
            File.WriteAllText(ignoredFile, "This should be ignored");
            Directory.CreateDirectory(testDir);
            Directory.CreateDirectory(ignoredDir);
            File.WriteAllText(Path.Combine(testDir, "file.txt"), "Include this");
            File.WriteAllText(Path.Combine(ignoredDir, "file.txt"), "Ignore this");

            // Step 4: Create .funcignore file
            var funcIgnorePath = Path.Combine(workingDir, ".funcignore");
            File.WriteAllText(funcIgnorePath, "ignored-file.txt\nignored-dir/\n*.log\n");

            // Step 5: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);

            // Step 6: Validate zip contents respect .funcignore
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            Assert.True(File.Exists(expectedZipPath), $"Zip file should exist at {expectedZipPath}");

            using var archive = ZipFile.OpenRead(expectedZipPath);
            var entries = archive.Entries.Select(e => e.FullName).ToList();
            
            // Should include test-file.txt and test-dir/
            Assert.Contains("test-file.txt", entries);
            Assert.Contains(entries, entry => entry.StartsWith("test-dir/"));
            
            // Should NOT include ignored files/directories
            Assert.DoesNotContain("ignored-file.txt", entries);
            Assert.DoesNotContain(entries, entry => entry.StartsWith("ignored-dir/"));
            Assert.DoesNotContain(".funcignore", entries); // .funcignore itself should be excluded
        }

        [Fact]
        public void Pack_InvalidFunctionApp_FailsWithExpectedError()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_InvalidFunctionApp_FailsWithExpectedError);
            
            // Don't initialize a function app - just try to pack an empty directory
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            // Should fail because there's no host.json
            packResult.Should().ExitWithNonZero();
            packResult.Should().HaveStdErrContaining("Can't find");
            packResult.Should().HaveStdErrContaining("host.json");
        }

        [Fact]
        public void Pack_WithAbsoluteOutputPath_CreatesZipAtPath()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_WithAbsoluteOutputPath_CreatesZipAtPath);
            var runtime = "node";
            var outputPath = Path.Combine(Path.GetTempPath(), $"{testName}.zip");
            
            try
            {
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

                // Step 3: Run pack command with absolute output path
                var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
                var packResult = funcPackCommand
                    .WithWorkingDirectory(workingDir)
                    .Execute(["-o", outputPath]);

                packResult.Should().ExitWith(0);
                packResult.Should().HaveStdOutContaining("Creating a new package");

                // Step 4: Verify zip file was created at absolute path
                Assert.True(File.Exists(outputPath), $"Zip file should exist at {outputPath}");
            }
            finally
            {
                // Cleanup
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        [Fact]
        public void Pack_LargeNumberOfFiles_CompletesSuccessfully()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_LargeNumberOfFiles_CompletesSuccessfully);
            var runtime = "node";
            
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

            // Step 3: Create many test files to simulate a larger project
            var testDataDir = Path.Combine(workingDir, "test-data");
            Directory.CreateDirectory(testDataDir);
            
            for (int i = 0; i < 100; i++)
            {
                File.WriteAllText(Path.Combine(testDataDir, $"file{i:D3}.txt"), $"Content of file {i}");
            }

            // Step 4: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 5: Verify zip file was created and contains expected number of files
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            Assert.True(File.Exists(expectedZipPath), $"Zip file should exist at {expectedZipPath}");

            using var archive = ZipFile.OpenRead(expectedZipPath);
            var testDataEntries = archive.Entries.Where(e => e.FullName.StartsWith("test-data/")).ToList();
            
            // Should contain all 100 test files
            Assert.True(testDataEntries.Count >= 100, $"Expected at least 100 test-data files, found {testDataEntries.Count}");
        }

        // TODO: Stretch goal - Azure CLI deployment test
        // This test would require Azure CLI and authentication setup
        // [Fact]
        // public void Pack_ThenDeploy_WithAzureCLI_DeploysSuccessfully()
        // {
        //     // This would test the full end-to-end scenario:
        //     // 1. Create function app
        //     // 2. Pack it
        //     // 3. Deploy using Azure CLI
        //     // 4. Verify deployment success
        //     // 
        //     // Implementation would require:
        //     // - Azure CLI installed and authenticated
        //     // - Azure subscription and resource group
        //     // - Function app resource creation
        //     // - az functionapp deployment source config-zip command
        //     // - Cleanup of Azure resources
        // }
    }
}