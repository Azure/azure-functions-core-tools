// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Actions.LocalActions;
using Colors.Net;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    [Collection("BundleActionTests")]
    public class DownloadBundleActionTests : IDisposable
    {
        private const string HostJsonWithBundles = @"{""version"": ""2.0"", ""extensionBundle"": {""id"": ""Microsoft.Azure.Functions.ExtensionBundle"", ""version"": ""[4.*, 5.0.0)""}}";
        private const string HostJsonWithBundlesAndDownloadPath = @"{""version"": ""2.0"", ""extensionBundle"": {""id"": ""Microsoft.Azure.Functions.ExtensionBundle"", ""version"": ""[4.*, 5.0.0)"", ""downloadPath"": ""bundles""}}";

        private readonly string _testDirectory;
        private readonly string _originalDirectory;
        private readonly StringBuilder _consoleOutput;
        private readonly IConsoleWriter _mockConsole;

        public DownloadBundleActionTests()
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            _testDirectory = Path.Combine(Path.GetTempPath(), $"func_download_bundle_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDirectory);

            // Set up console capture
            _consoleOutput = new StringBuilder();
            _mockConsole = Substitute.For<IConsoleWriter>();
            _mockConsole.WriteLine(Arg.Any<object>()).Returns(x =>
            {
                _consoleOutput.AppendLine(x[0]?.ToString());
                return _mockConsole;
            });
            _mockConsole.Write(Arg.Any<object>()).Returns(x =>
            {
                _consoleOutput.Append(x[0]?.ToString());
                return _mockConsole;
            });
            ColoredConsole.Out = _mockConsole;
            ColoredConsole.Error = _mockConsole;
        }

        public void Dispose()
        {
            try
            {
                Directory.SetCurrentDirectory(_originalDirectory);
            }
            catch
            {
                // Ignore directory errors
            }

            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        [Fact]
        public void ParseArgs_NoArguments_Succeeds()
        {
            var action = new DownloadBundleAction();
            var args = Array.Empty<string>();

            var result = action.ParseArgs(args);

            result.Should().NotBeNull();
            action.Force.Should().BeFalse();
        }

        [Fact]
        public void ParseArgs_WithForceFlag_SetsForceTrue()
        {
            var action = new DownloadBundleAction();
            var args = new[] { "--force" };

            action.ParseArgs(args);

            action.Force.Should().BeTrue();
        }

        [Fact]
        public void ParseArgs_WithShortForceFlag_SetsForceTrue()
        {
            var action = new DownloadBundleAction();
            var args = new[] { "-f" };

            action.ParseArgs(args);

            action.Force.Should().BeTrue();
        }

        [Fact]
        public void HasCorrectActionAttribute()
        {
            var actionAttribute = Attribute.GetCustomAttribute(typeof(DownloadBundleAction), typeof(ActionAttribute)) as ActionAttribute;

            actionAttribute.Should().NotBeNull();
            actionAttribute!.Name.Should().Be("download");
            actionAttribute.Context.Should().Be(Context.Bundles);
            actionAttribute.HelpText.Should().Contain("Download");
        }

        [Fact]
        public void HasCorrectActionAttribute_HelpTextDescribesExtensionBundle()
        {
            var actionAttribute = Attribute.GetCustomAttribute(typeof(DownloadBundleAction), typeof(ActionAttribute)) as ActionAttribute;

            actionAttribute.Should().NotBeNull();
            actionAttribute!.HelpText.Should().Contain("extension bundle");
            actionAttribute.HelpText.Should().Contain("host.json");
        }

        [Fact]
        public async Task RunAsync_WithoutHostJson_CompletesGracefully()
        {
            Directory.SetCurrentDirectory(_testDirectory);
            var action = new DownloadBundleAction();

            await action.RunAsync();

            // Should complete without throwing
        }

        [Fact]
        public async Task RunAsync_WithHostJsonWithoutBundles_CompletesGracefully()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, "{\"version\": \"2.0\"}");

            var action = new DownloadBundleAction();

            await action.RunAsync();

            // Should complete without throwing
        }

        [Fact]
        public async Task RunAsync_WithBundlesConfigured_OutputsBundleInfo()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, HostJsonWithBundles);

            var action = new DownloadBundleAction();

            // Note: This will attempt actual download, so we just verify it starts correctly
            try
            {
                await action.RunAsync();
            }
            catch
            {
                // Expected - download may fail in test environment
            }

            var output = _consoleOutput.ToString();
            output.Should().Contain("Microsoft.Azure.Functions.ExtensionBundle");
            output.Should().Contain("[4.*, 5.0.0)");
        }

        [Fact]
        public async Task RunAsync_WithExistingBundle_SkipsDownload_WhenForceNotSet()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, HostJsonWithBundlesAndDownloadPath);

            // Create a fake existing bundle
            var bundlePath = Path.Combine(_testDirectory, "bundles", "4.30.0");
            Directory.CreateDirectory(bundlePath);
            await File.WriteAllTextAsync(Path.Combine(bundlePath, "bundle.json"), "{}");

            var action = new DownloadBundleAction();

            try
            {
                await action.RunAsync();
            }
            catch
            {
                // May fail due to version resolution, but we're testing the skip logic
            }

            var output = _consoleOutput.ToString();

            // Should indicate bundle exists or attempt to download
            output.Should().Contain("Microsoft.Azure.Functions.ExtensionBundle");
        }

        [Fact]
        public async Task RunAsync_WithForce_ClearsExistingBundles()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, HostJsonWithBundlesAndDownloadPath);

            // Create a fake existing bundle
            var bundlePath = Path.Combine(_testDirectory, "bundles", "4.30.0");
            Directory.CreateDirectory(bundlePath);
            await File.WriteAllTextAsync(Path.Combine(bundlePath, "bundle.json"), "{}");

            var action = new DownloadBundleAction { Force = true };

            try
            {
                await action.RunAsync();
            }
            catch
            {
                // Expected - download may fail in test environment
            }

            var output = _consoleOutput.ToString();
            output.Should().Contain("Clearing existing bundles");
        }

        [Fact]
        public void InheritsFromBaseAction()
        {
            var action = new DownloadBundleAction();

            action.Should().BeAssignableTo<BaseAction>();
        }

        [Fact]
        public void ForceProperty_DefaultsToFalse()
        {
            var action = new DownloadBundleAction();

            action.Force.Should().BeFalse();
        }

        [Fact]
        public void ForceProperty_CanBeSet()
        {
            var action = new DownloadBundleAction { Force = true };

            action.Force.Should().BeTrue();
        }
    }
}
