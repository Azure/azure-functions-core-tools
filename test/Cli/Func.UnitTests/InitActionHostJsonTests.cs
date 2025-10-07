// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Func.UnitTests.ActionsTests
{
    public class InitActionHostJsonTests : IDisposable
    {
        private readonly string _testDir;

        public InitActionHostJsonTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDir);
            Directory.SetCurrentDirectory(_testDir);
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet)]
        [InlineData(WorkerRuntime.DotnetIsolated)]
        [InlineData(WorkerRuntime.Node)]
        [InlineData(WorkerRuntime.Python)]
        [InlineData(WorkerRuntime.Java)]
        [InlineData(WorkerRuntime.Powershell)]
        [InlineData(WorkerRuntime.Custom)]
        [InlineData(WorkerRuntime.None)]
        public void DeleteOrOverwriteHostJsonIfOnlyFile_DeletesHostJson_WhenOnlyFilePresent(WorkerRuntime runtime)
        {
            // Arrange
            var hostJsonPath = Path.Combine(_testDir, "host.json");
            File.WriteAllText(hostJsonPath, "{ \"test\": true }");

            // Act
            InitAction.DeleteOrOverwriteHostJsonIfOnlyFile();

            // Assert
            Assert.False(File.Exists(hostJsonPath));
        }

        [Fact]
        public void DeleteOrOverwriteHostJsonIfOnlyFile_DoesNotDeleteHostJson_WhenOtherFilesPresent()
        {
            // Arrange
            var hostJsonPath = Path.Combine(_testDir, "host.json");
            var otherFilePath = Path.Combine(_testDir, "other.txt");
            File.WriteAllText(hostJsonPath, "{ \"test\": true }");
            File.WriteAllText(otherFilePath, "other");

            // Act
            InitAction.DeleteOrOverwriteHostJsonIfOnlyFile();

            // Assert
            Assert.True(File.Exists(hostJsonPath));
            Assert.True(File.Exists(otherFilePath));
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
    }
}
