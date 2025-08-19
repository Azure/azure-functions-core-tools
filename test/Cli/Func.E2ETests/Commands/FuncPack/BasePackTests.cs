// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    internal static class BasePackTests
    {
        internal static void TestBasicPackFunctionality(string workingDir, string testName, string funcPath, ITestOutputHelper log, string[] filesToValidate, string[]? logStatementsToValidate = null)
        {
            // Run pack command
            var funcPackCommand = new FuncPackCommand(funcPath, testName, log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            // Verify pack succeeded
            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            // Find any zip files in the working directory
            var zipFiles = Directory.GetFiles(workingDir, "*.zip");

            // Verify at least one zip file exists
            Assert.True(zipFiles.Length > 0, $"No zip files found in {workingDir}");

            // Log all found zip files
            foreach (var zipFile in zipFiles)
            {
                log?.WriteLine($"Found zip file: {Path.GetFileName(zipFile)}");
            }

            // Verify the first zip file has some content (should be > 0 bytes)
            var zipFileInfo = new FileInfo(zipFiles.First());
            Assert.True(zipFileInfo.Length > 0, $"Zip file {zipFileInfo.FullName} exists but is empty");

            // Validate the contents of the zip file
            packResult.Should().ValidateZipContents(zipFiles.First(), filesToValidate, log);

            File.Delete(zipFiles.First()); // Clean up the zip file after validation
        }

        internal static async Task TestNoBuildCustomOutputPackFunctionality(
            string projectDir,
            string testName,
            string funcPath,
            ITestOutputHelper log,
            string zipOutputDirectory,
            string[] filesToValidate)
        {
            // Ensure publish output exists for --no-build scenario
            var randomDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var outputPath = Path.Combine(randomDir, "output");
            var exe = new Executable("dotnet", $"publish --output \"{outputPath}\"", workingDirectory: projectDir);
            var exitCode = await exe.RunAsync();
            exitCode.Should().Be(0);

            // Run func pack pointing at publish output directory, no build, and custom output location
            var funcPackCommand = new FuncPackCommand(funcPath, testName, log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(projectDir)
                .Execute([outputPath, "--no-build", "--output", zipOutputDirectory]);

            // Verify pack succeeded and build was skipped
            packResult.Should().ExitWith(0);
            packResult.Should().NotHaveStdOutContaining("Building .NET project...");
            packResult.Should().HaveStdOutContaining("Skipping build event for functions project (--no-build).");

            // Find any zip files in the specified output directory
            var zipFiles = Directory.GetFiles(zipOutputDirectory, "*.zip");
            Assert.True(zipFiles.Length > 0, $"No zip files found in {zipOutputDirectory}");

            // Validate zip contents
            packResult.Should().ValidateZipContents(zipFiles.First(), filesToValidate, log);

            // Clean up
            File.Delete(zipFiles.First());
        }
    }
}
