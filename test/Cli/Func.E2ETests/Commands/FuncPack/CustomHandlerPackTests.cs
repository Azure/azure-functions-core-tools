// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Custom)]
    public class CustomHandlerPackTests : BaseE2ETests
    {
        public CustomHandlerPackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string CustomHandlerProjectPath => Path.Combine(TestProjectDirectory, "TestCustomHandlerProject");

        [Fact]
        public void Pack_CustomHandler_TurnsBitExecutable()
        {
            var testName = nameof(Pack_CustomHandler_TurnsBitExecutable);

            var packResult = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(CustomHandlerProjectPath)
                .Execute([]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            var zipFiles = Directory.GetFiles(CustomHandlerProjectPath, "*.zip");
            Assert.True(zipFiles.Length > 0, $"No zip files found in {CustomHandlerProjectPath}");

            var zipPath = zipFiles.First();

            packResult.Should().ValidateZipContents(
                zipPath,
                new[]
                {
                    "host.json",
                    "GoCustomHandlers"
                },
                Log);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("GoCustomHandlers"));
                entry.Should().NotBeNull("GoCustomHandlers should be present in the packaged zip");

                int permissions = (entry!.ExternalAttributes >> 16) & 0xFFFF;
                permissions.Should().Be(Convert.ToInt32("100777", 8), "GoCustomHandlers should be marked as executable in the zip");
            }

            File.Delete(zipPath);
        }
    }
}
