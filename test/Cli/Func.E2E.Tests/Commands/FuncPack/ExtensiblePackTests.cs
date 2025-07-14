// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    /// <summary>
    /// Extensible pack tests that demonstrate how to use PackTestHelpers for maintainable testing.
    /// This class shows how new runtimes and scenarios can be easily added to the test suite.
    /// </summary>
    public class ExtensiblePackTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [MemberData(nameof(GetAllSupportedRuntimes))]
        public void Pack_AllSupportedRuntimes_CreatesValidZip(string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_AllSupportedRuntimes_CreatesValidZip)}_{runtime}";
            
            // Skip .NET in-process for local builds as it's not supported
            var config = PackTestHelpers.SupportedRuntimes[runtime];
            if (runtime == "dotnet" && !config.SupportsLocalBuild)
            {
                // Test remote build instead
                Pack_DotnetWithRemoteBuild_CreatesValidZip(runtime);
                return;
            }

            // Step 1: Create function app using helper
            var setup = PackTestHelpers.CreateFunctionApp(FuncPath, workingDir, runtime, testName, Log);

            // Step 2: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 3: Validate zip using helper
            var expectedZipPath = PackTestHelpers.GetExpectedZipPath(workingDir);
            var validation = PackTestHelpers.ValidatePackOutput(expectedZipPath, config, Log);

            Assert.True(validation.IsValid, $"Zip validation failed: {validation.Error}");
            Assert.True(validation.EntryCount > 0, "Zip should contain files");
            Assert.Empty(validation.MissingFiles);
            Assert.Empty(validation.UnexpectedFiles);
        }

        [Theory]
        [MemberData(nameof(GetRuntimesWithNativeDepsSupport))]
        public void Pack_RuntimesWithNativeDeps_BuildsSuccessfully(string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_RuntimesWithNativeDeps_BuildsSuccessfully)}_{runtime}";
            
            // Step 1: Create function app
            var setup = PackTestHelpers.CreateFunctionApp(FuncPath, workingDir, runtime, testName, Log);

            // Step 2: Run pack with native dependencies
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--build-native-deps"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Step 3: Validate output
            var expectedZipPath = PackTestHelpers.GetExpectedZipPath(workingDir);
            var validation = PackTestHelpers.ValidatePackOutput(expectedZipPath, setup.Config, Log);

            Assert.True(validation.IsValid, $"Zip validation failed: {validation.Error}");
        }

        [Theory]
        [MemberData(nameof(GetStandardIgnorePatterns))]
        public void Pack_WithStandardIgnorePatterns_ExcludesCorrectFiles(string patternName)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_WithStandardIgnorePatterns_ExcludesCorrectFiles)}_{patternName}";
            var runtime = "node"; // Use Node.js as representative runtime
            
            // Step 1: Create function app
            var setup = PackTestHelpers.CreateFunctionApp(FuncPath, workingDir, runtime, testName, Log);

            // Step 2: Create test files for ignore testing
            PackTestHelpers.CreateTestFilesForIgnoreTesting(workingDir, patternName);

            // Step 3: Create .funcignore with standard pattern
            var funcIgnorePath = Path.Combine(workingDir, ".funcignore");
            var ignorePattern = PackTestHelpers.StandardFuncIgnorePatterns[patternName];
            File.WriteAllText(funcIgnorePath, ignorePattern);

            // Step 4: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);

            // Step 5: Validate that ignored files are excluded
            var expectedZipPath = PackTestHelpers.GetExpectedZipPath(workingDir);
            var validation = PackTestHelpers.ValidatePackOutput(expectedZipPath, setup.Config, Log);

            Assert.True(validation.IsValid, $"Zip validation failed: {validation.Error}");
            
            // Verify specific ignore behavior based on pattern
            switch (patternName)
            {
                case "basic":
                    Assert.DoesNotContain(validation.Entries, e => e.EndsWith(".log"));
                    Assert.DoesNotContain(validation.Entries, e => e.EndsWith(".tmp"));
                    Assert.DoesNotContain(validation.Entries, e => e.StartsWith("temp/"));
                    break;
                case "python":
                    Assert.DoesNotContain(validation.Entries, e => e.Contains("__pycache__"));
                    Assert.DoesNotContain(validation.Entries, e => e.EndsWith(".pyc"));
                    Assert.DoesNotContain(validation.Entries, e => e.StartsWith(".venv/"));
                    break;
                case "node":
                    Assert.DoesNotContain(validation.Entries, e => e.StartsWith("node_modules/"));
                    Assert.DoesNotContain(validation.Entries, e => e.EndsWith(".js.map"));
                    break;
            }
        }

        [Fact]
        public void Pack_CustomScenario_DemonstratesExtensibility()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_CustomScenario_DemonstratesExtensibility);
            var runtime = "custom";
            
            // This test demonstrates how to create custom scenarios using the helper framework
            
            // Step 1: Create function app with custom runtime
            var setup = PackTestHelpers.CreateFunctionApp(FuncPath, workingDir, runtime, testName, Log);

            // Step 2: Create custom project structure
            var srcDir = Path.Combine(workingDir, "src");
            var configDir = Path.Combine(workingDir, "config");
            var docsDir = Path.Combine(workingDir, "docs");
            
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(docsDir);
            
            File.WriteAllText(Path.Combine(srcDir, "handler.js"), "// Custom handler");
            File.WriteAllText(Path.Combine(configDir, "app.json"), "{}");
            File.WriteAllText(Path.Combine(docsDir, "README.md"), "# Documentation");

            // Step 3: Create custom .funcignore that includes docs but excludes config
            var funcIgnorePath = Path.Combine(workingDir, ".funcignore");
            File.WriteAllText(funcIgnorePath, "config/\n*.log\ntemp/\n");

            // Step 4: Run pack command
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            packResult.Should().ExitWith(0);

            // Step 5: Validate custom scenario expectations
            var expectedZipPath = PackTestHelpers.GetExpectedZipPath(workingDir);
            var validation = PackTestHelpers.ValidatePackOutput(
                expectedZipPath, 
                setup.Config, 
                Log,
                additionalExpectedFiles: new[] { "src/", "docs/" },
                additionalIgnoredFiles: new[] { "config/" }
            );

            Assert.True(validation.IsValid, $"Zip validation failed: {validation.Error}");
            
            // Verify custom expectations
            Assert.Contains(validation.Entries, e => e.StartsWith("src/"));
            Assert.Contains(validation.Entries, e => e.StartsWith("docs/"));
            Assert.DoesNotContain(validation.Entries, e => e.StartsWith("config/"));
        }

        private void Pack_DotnetWithRemoteBuild_CreatesValidZip(string runtime)
        {
            var workingDir = WorkingDirectory;
            var testName = $"{nameof(Pack_DotnetWithRemoteBuild_CreatesValidZip)}_{runtime}";
            
            // Step 1: Create .NET function app
            var setup = PackTestHelpers.CreateFunctionApp(FuncPath, workingDir, runtime, testName, Log);

            // Step 2: Run pack with remote build option
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--build-option", "remote"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");
            packResult.Should().HaveStdOutContaining("Performing remote build");

            // Step 3: Validate zip
            var expectedZipPath = PackTestHelpers.GetExpectedZipPath(workingDir);
            var validation = PackTestHelpers.ValidatePackOutput(expectedZipPath, setup.Config, Log);

            Assert.True(validation.IsValid, $"Zip validation failed: {validation.Error}");
        }

        // Data providers for parameterized tests
        public static IEnumerable<object[]> GetAllSupportedRuntimes()
        {
            return PackTestHelpers.SupportedRuntimes.Keys.Select(runtime => new object[] { runtime });
        }

        public static IEnumerable<object[]> GetRuntimesWithNativeDepsSupport()
        {
            return PackTestHelpers.SupportedRuntimes
                .Where(kvp => kvp.Value.SupportsNativeDeps)
                .Select(kvp => new object[] { kvp.Key });
        }

        public static IEnumerable<object[]> GetStandardIgnorePatterns()
        {
            return PackTestHelpers.StandardFuncIgnorePatterns.Keys.Select(pattern => new object[] { pattern });
        }
    }
}