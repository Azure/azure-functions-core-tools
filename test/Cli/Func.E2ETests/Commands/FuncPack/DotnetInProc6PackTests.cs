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
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc6PackTests : BaseE2ETests
    {
        public DotnetInProc6PackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string Dotnet6ProjectPath => Path.Combine(TestProjectDirectory, "TestNet6InProcProject");

        [Fact]
        public void Pack_Dotnet6InProc_WorksAsExpected()
        {
            var testName = nameof(Pack_Dotnet6InProc_WorksAsExpected);

            var logsToValidate = new[]
            {
                "Building .NET project...",
                "Determining projects to restore..."
            };

            BasePackTests.TestBasicPackFunctionality(
                Dotnet6ProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.pdb"),
                    Path.Combine("Dotnet6InProc", "function.json"),
                    Path.Combine("bin", "runtimes", "browser", "lib", "net6.0", "System.Text.Encodings.Web.dll")
                },
                logsToValidate);
        }

        [Fact]
        public async Task Pack_Dotnet6InProc_CustomOutput_NoBuild()
        {
            var testName = nameof(Pack_Dotnet6InProc_CustomOutput_NoBuild);

            await BasePackTests.TestNoBuildCustomOutputPackFunctionality(
                Dotnet6ProjectPath,
                testName,
                FuncPath,
                Log,
                WorkingDirectory,
                new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.pdb"),
                    Path.Combine("Dotnet6InProc", "function.json"),
                    Path.Combine("bin", "runtimes", "browser", "lib", "net6.0", "System.Text.Encodings.Web.dll")
                });
        }

        [Fact]
        public void Pack_Dotnet6InProc_PreserveExecutables_SetsBit()
        {
            var testName = nameof(Pack_Dotnet6InProc_PreserveExecutables_SetsBit);
            var execRelativePath = "TurnThisExecutable";

            var packResult = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(Dotnet6ProjectPath)
                .Execute(["--preserve-executables", execRelativePath]);

            packResult.Should().ExitWith(0);

            var zipFiles = Directory.GetFiles(Dotnet6ProjectPath, "*.zip");
            Assert.True(zipFiles.Length > 0, $"No zip files found in {Dotnet6ProjectPath}");
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
