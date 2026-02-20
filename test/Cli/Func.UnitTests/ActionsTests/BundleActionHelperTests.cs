// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions;
using FluentAssertions;
using Microsoft.Azure.WebJobs.Script;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    [Collection("BundleActionTests")]
    public class BundleActionHelperTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string _originalDirectory;

        public BundleActionHelperTests()
        {
            _originalDirectory = Environment.CurrentDirectory;
            _testDirectory = Path.Combine(Path.GetTempPath(), "BundleActionHelperTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testDirectory);
            Environment.CurrentDirectory = _testDirectory;
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = _originalDirectory;
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to delete test directory '{_testDirectory}': {ex}");
                }
            }
        }

        [Fact]
        public void TryGetBundleContext_ReturnsFalse_WhenHostJsonMissing()
        {
            var hostJsonPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            if (File.Exists(hostJsonPath))
            {
                File.Delete(hostJsonPath);
            }

            var result = BundleActionHelper.TryGetBundleContext(out var manager, out var options, out var basePath);

            result.Should().BeFalse();
            options.Should().BeNull();
            basePath.Should().BeNull();
            manager.Should().BeNull();
        }

        [Fact]
        public void TryGetBundleContext_UsesDownloadPath_FromHostJson()
        {
            var customPath = Path.Combine(_testDirectory, "custom-bundles");
            Directory.CreateDirectory(customPath);
            var hostJsonPath = Path.Combine(_testDirectory, "host.json");
            var hostJsonContent = @"{
                    ""version"": ""2.0"",
                    ""extensionBundle"": {
                        ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
                        ""version"": ""[4.*, 5.0.0)"",
                        ""downloadPath"": """ + customPath.Replace("\\", "\\\\") + @"""
                    }
                }";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            var result = BundleActionHelper.TryGetBundleContext(out var manager, out var options, out var basePath);

            result.Should().BeTrue();
            manager.Should().NotBeNull();
            options.Should().NotBeNull();
            basePath.Should().Be(customPath);
        }
    }
}
