// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using System.IO.Compression;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Powershell)]
    public class PowershellPackTests : BaseE2ETests
    {
        public PowershellPackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string PowershellProjectPath => Path.Combine(TestProjectDirectory, "TestPowershellProject");

        [Fact]
        public void Pack_Powershell_WorksAsExpected()
        {
            var testName = nameof(Pack_Powershell_WorksAsExpected);

            BasePackTests.TestBasicPackFunctionality(
                PowershellProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "requirements.psd1",
                    Path.Combine("HttpTrigger", "run.ps1"),
                    "profile.ps1",
                    Path.Combine("HttpTrigger", "function.json")
                });
        }

        [Fact]
        public void Pack_Powershell_CustomOutput_NoBuild()
        {
            var testName = nameof(Pack_Powershell_CustomOutput_NoBuild);

            // Warm up (restore extensions) then delete zip
            var warmup = new FuncPackCommand(FuncPath, testName + "_Warmup", Log)
                .WithWorkingDirectory(PowershellProjectPath)
                .Execute([]);
            warmup.Should().ExitWith(0);
            foreach (var zip in Directory.GetFiles(PowershellProjectPath, "*.zip"))
            {
                File.Delete(zip);
            }

            var customOutput = "pscustom";
            var packNoBuild = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(PowershellProjectPath)
                .Execute(["--no-build", "-o", customOutput]);

            packNoBuild.Should().ExitWith(0);
            packNoBuild.Should().HaveStdOutContaining("Skipping build event for functions project (--no-build).");

            var expectedZip = Path.Combine(PowershellProjectPath, customOutput + ".zip");
            File.Exists(expectedZip).Should().BeTrue();
            packNoBuild.Should().ValidateZipContents(expectedZip, new[] { "host.json", "requirements.psd1" }, Log);
            File.Delete(expectedZip);
        }

        [Fact]
        public void Pack_Powershell_NoBuild_BehaviorUnchanged()
        {
            var testName = nameof(Pack_Powershell_NoBuild_BehaviorUnchanged);

            // Clean any previous zips
            foreach (var zip in Directory.GetFiles(PowershellProjectPath, "*.zip"))
            {
                File.Delete(zip);
            }

            // Baseline: regular pack
            var regular = new FuncPackCommand(FuncPath, testName + "_Regular", Log)
                .WithWorkingDirectory(PowershellProjectPath)
                .Execute([]);
            regular.Should().ExitWith(0);

            var baselineZip = Directory.GetFiles(PowershellProjectPath, "*.zip").FirstOrDefault();
            baselineZip.Should().NotBeNull();
            regular.Should().ValidateZipContents(baselineZip!, new[]
            {
                "host.json",
                "requirements.psd1",
                Path.Combine("HttpTrigger", "run.ps1"),
                "profile.ps1",
                Path.Combine("HttpTrigger", "function.json")
            }, Log);

            // Remove zip to avoid interference
            File.Delete(baselineZip!);

            // Now run with --no-build and validate contents are the same (behavior unchanged)
            var nobuild = new FuncPackCommand(FuncPath, testName + "_NoBuild", Log)
                .WithWorkingDirectory(PowershellProjectPath)
                .Execute(["--no-build"]);
            nobuild.Should().ExitWith(0);
            nobuild.Should().HaveStdOutContaining("Skipping build event for functions project (--no-build).");

            var nobuildZip = Directory.GetFiles(PowershellProjectPath, "*.zip").FirstOrDefault();
            nobuildZip.Should().NotBeNull();
            nobuild.Should().ValidateZipContents(nobuildZip!, new[]
            {
                "host.json",
                "requirements.psd1",
                Path.Combine("HttpTrigger", "run.ps1"),
                "profile.ps1",
                Path.Combine("HttpTrigger", "function.json")
            }, Log);

            File.Delete(nobuildZip!);
        }

        [Fact]
        public void Pack_Powershell_PreserveExecutables_SetsBit()
        {
            var testName = nameof(Pack_Powershell_PreserveExecutables_SetsBit);
            var execRelativePath = "TurnThisExecutable";

            var packResult = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(PowershellProjectPath)
                .Execute(["--preserve-executables", execRelativePath]);

            packResult.Should().ExitWith(0);

            var zipFiles = Directory.GetFiles(PowershellProjectPath, "*.zip");
            Assert.True(zipFiles.Length > 0, $"No zip files found in {PowershellProjectPath}");
            var zipPath = zipFiles.First();

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith(execRelativePath));
                entry.Should().NotBeNull();
                int permissions = (entry!.ExternalAttributes >> 16) & 0xFFFF;
                permissions.Should().Be(Convert.ToInt32("100777", 8));
            }

            File.Delete(zipPath);
        }
    }
}
