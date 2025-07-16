// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Fixtures;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    internal static class BasePackTests
    {
        internal static void TestBasicPackFunctionality(string workingDir, string testName, BaseFunctionAppFixture fixture, string[] filesToValidate)
        {
            // Run pack command
            var funcPackCommand = new FuncPackCommand(fixture.FuncPath, testName, fixture.Log ?? throw new ArgumentNullException(nameof(fixture.Log)));
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
                fixture.Log?.WriteLine($"Found zip file: {Path.GetFileName(zipFile)}");
            }

            // Verify the first zip file has some content (should be > 0 bytes)
            var zipFileInfo = new FileInfo(zipFiles.First());
            Assert.True(zipFileInfo.Length > 0, $"Zip file {zipFileInfo.FullName} exists but is empty");

            // Validate the contents of the zip file
            packResult.Should().ValidateZipContents(zipFiles.First(), filesToValidate, fixture.Log);
        }
    }
}
