// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
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

            if (logStatementsToValidate is not null)
            {
                foreach (var s in logStatementsToValidate)
                {
                    packResult.Should().HaveStdOutContaining(s);
                }
            }

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

        internal static async Task TestDotnetNoBuildCustomOutputPackFunctionality(
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
            var exe = new Executable("dotnet", $"publish --output {outputPath}", workingDirectory: projectDir);
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

        internal static void TestPackWithDirectoryBuildProps(
            string projectAbsoluteDir,
            bool noBuild,
            string testName,
            string funcPath,
            ITestOutputHelper log,
            string[] filesToValidate,
            string[] logsToValidate)
        {
            // Create Directory.Build.props inside the project directory to define ArtifactsPath.
            var path = Path.GetFullPath(projectAbsoluteDir);
            var propsPath = Path.Combine(path, "Directory.Build.props");
            var packagesPropsPath = Path.Combine(path, "Directory.Packages.props");
            var artifactsDirName = "customArtifacts";
            var artifactsDirFullPath = Path.Combine(path, artifactsDirName);

            // Clean up any previous artifacts dir or zip files
            if (Directory.Exists(artifactsDirFullPath))
            {
                Directory.Delete(artifactsDirFullPath, true);
            }

            foreach (var zip in Directory.GetFiles(path, "*.zip"))
            {
                File.Delete(zip);
            }

            var propsContent = $"""
                <Project>
                  <PropertyGroup>
                    <ArtifactsPath>$(ProjectDir){artifactsDirName}</ArtifactsPath>
                    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
                    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
                  </PropertyGroup>
                </Project>
                """;

            var packagesPropsContent = """
                <Project>
                  <!-- Empty file to override repository Directory.Packages.props -->
                </Project>
                """;

            File.WriteAllText(propsPath, propsContent);
            File.WriteAllText(packagesPropsPath, packagesPropsContent);

            try
            {
                // If using --no-build, we need to pre-build the project first
                if (noBuild)
                {
                    log?.WriteLine("Pre-building project for --no-build test...");

                    var buildResult = RunDotNetPublish(projectAbsoluteDir, artifactsDirFullPath, log!);
                    if (buildResult != 0)
                    {
                        throw new Exception($"Pre-build failed with exit code {buildResult}");
                    }

                    // Verify the build output exists
                    if (!Directory.Exists(artifactsDirFullPath))
                    {
                        throw new Exception($"Build output directory not created: {artifactsDirFullPath}");
                    }

                    log?.WriteLine($"Pre-build completed successfully. Output in: {artifactsDirFullPath}");
                }

                // Execute pack
                var funcPackCommand = new FuncPackCommand(funcPath, testName, log!);
                var args = noBuild ? new[] { "--no-build" } : Array.Empty<string>();
                var packResult = funcPackCommand
                    .WithWorkingDirectory(projectAbsoluteDir)
                    .Execute(args);

                // Basic success assertions
                packResult.Should().ExitWith(0);
                packResult.Should().HaveStdOutContaining("Creating a new package");

                // Only expect build message when not using --no-build
                if (!noBuild)
                {
                    packResult.Should().HaveStdOutContaining("Building .NET project...");
                }

                // Validate any additional expected log statements
                foreach (var expectedLog in logsToValidate)
                {
                    packResult.Should().HaveStdOutContaining(expectedLog);
                }

                // Find any zip files in the project directory
                var zipFiles = Directory.GetFiles(projectAbsoluteDir, "*.zip");
                Assert.True(zipFiles.Length > 0, $"No zip files found in {projectAbsoluteDir}");
                var zipFile = zipFiles.First();
                log?.WriteLine($"Found zip file: {Path.GetFileName(zipFile)}");

                // Validate contents
                packResult.Should().ValidateZipContents(zipFile, filesToValidate, log);

                // Ensure artifacts directory exists after
                Assert.True(Directory.Exists(artifactsDirFullPath));

                File.Delete(zipFile);
            }
            finally
            {
                // Cleanup: remove props files and artifacts directory
                if (File.Exists(propsPath))
                {
                    File.Delete(propsPath);
                }

                if (File.Exists(packagesPropsPath))
                {
                    File.Delete(packagesPropsPath);
                }

                if (Directory.Exists(artifactsDirFullPath))
                {
                    Directory.Delete(artifactsDirFullPath, true);
                }
            }
        }

        private static int RunDotNetPublish(string projectDirectory, string outputPath, ITestOutputHelper log)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"publish --output \"{outputPath}\"",
                    WorkingDirectory = projectDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return -1;
                }

                // Log output for debugging
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    log?.WriteLine($"Build output: {output}");
                }

                if (!string.IsNullOrEmpty(error))
                {
                    log?.WriteLine($"Build error: {error}");
                }

                return process.ExitCode;
            }
            catch (Exception ex)
            {
                log?.WriteLine($"Exception during build: {ex.Message}");
                return -1;
            }
        }

        internal static void TestPackWithPathArgument(
            string funcInvocationWorkingDir,
            string projectAbsoluteDir,
            string pathArgumentToPass,
            string testName,
            string funcPath,
            ITestOutputHelper log,
            string[] filesToValidate)
        {
            var expectedZip = Path.Combine(funcInvocationWorkingDir, Path.GetFileName(projectAbsoluteDir) + ".zip");
            if (File.Exists(expectedZip))
            {
                File.Delete(expectedZip);
            }

            var funcPackCommand = new FuncPackCommand(funcPath, testName, log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(funcInvocationWorkingDir)
                .Execute(new[] { pathArgumentToPass });

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            File.Exists(expectedZip).Should().BeTrue($"Expected package at {expectedZip} was created.");

            packResult.Should().ValidateZipContents(expectedZip, filesToValidate, log);

            File.Delete(expectedZip);
        }
    }
}
