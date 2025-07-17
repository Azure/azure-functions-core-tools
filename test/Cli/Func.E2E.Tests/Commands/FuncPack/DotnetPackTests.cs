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
    /// Tests for func pack command specifically for .NET runtimes.
    /// .NET has special handling where in-process doesn't support pack for local builds,
    /// but isolated and remote builds are supported.
    /// </summary>
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetPackTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Pack_DotnetInProcess_ThrowsExpectedException()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_DotnetInProcess_ThrowsExpectedException);
            
            // Step 1: Initialize .NET in-process function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "dotnet"]);

            initResult.Should().ExitWith(0);
            initResult.Should().HaveStdOutContaining("Writing host.json");
            initResult.Should().HaveStdOutContaining("Writing local.settings.json");

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "HttpTrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command (should fail for .NET in-process)
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            // .NET in-process should fail with specific error message
            packResult.Should().ExitWithNonZero();
            packResult.Should().HaveStdErrContaining("Pack command doesn't work for dotnet functions");
        }

        [Fact]
        public void Pack_DotnetInProcess_WithRemoteBuild_CreatesZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_DotnetInProcess_WithRemoteBuild_CreatesZip);
            
            // Step 1: Initialize .NET in-process function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "dotnet"]);

            initResult.Should().ExitWith(0);

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "HttpTrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with remote build option
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--build-option", "remote"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");
            packResult.Should().HaveStdOutContaining("Performing remote build for functions project");

            // Step 4: Verify zip file was created
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }

    /// <summary>
    /// Tests for func pack command specifically for .NET Isolated runtime.
    /// </summary>
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class DotnetIsolatedPackTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Pack_DotnetIsolated_WithNoBuild_CreatesZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_DotnetIsolated_WithNoBuild_CreatesZip);
            
            // Step 1: Initialize .NET isolated function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "dotnet-isolated"]);

            initResult.Should().ExitWith(0);
            initResult.Should().HaveStdOutContaining("Writing host.json");
            initResult.Should().HaveStdOutContaining("Writing local.settings.json");

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "HttpTrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with --no-build flag
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--no-build"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");
            packResult.Should().HaveStdOutContaining("Skipping build event for functions project");

            // Step 4: Verify zip file was created
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Pack_DotnetIsolated_WithRemoteBuild_CreatesZip()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_DotnetIsolated_WithRemoteBuild_CreatesZip);
            
            // Step 1: Initialize .NET isolated function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "dotnet-isolated"]);

            initResult.Should().ExitWith(0);

            // Step 2: Create a function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "HttpTrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Step 3: Run pack command with remote build option
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var packResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--build-option", "remote"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");
            packResult.Should().HaveStdOutContaining("Performing remote build for functions project");

            // Step 4: Verify zip file was created
            var expectedZipPath = Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}.zip");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (expectedZipPath, new[] { string.Empty }) // Just check file exists
            };
            packResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}