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
    public class ListBundleActionTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string _originalDirectory;
        private readonly StringBuilder _consoleOutput;
        private readonly IConsoleWriter _mockConsole;

        public ListBundleActionTests()
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            _testDirectory = Path.Combine(Path.GetTempPath(), $"func_list_bundle_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDirectory);

            // Set up console capture
            _consoleOutput = new StringBuilder();
            _mockConsole = Substitute.For<IConsoleWriter>();

            // Handle any WriteLine call regardless of argument type
            _mockConsole.WriteLine(Arg.Any<object>()).Returns(x =>
            {
                _consoleOutput.AppendLine(x[0]?.ToString());
                return _mockConsole;
            });

            // Handle Write calls
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
                // Ignore directory errors, we'll clean up anyway
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
            var action = new ListBundleAction();
            var args = Array.Empty<string>();

            var result = action.ParseArgs(args);

            result.Should().NotBeNull();
        }

        [Fact]
        public void ParseArgs_WithUnknownArguments_Succeeds()
        {
            var action = new ListBundleAction();
            var args = new[] { "--unknown", "value" };

            var result = action.ParseArgs(args);

            result.Should().NotBeNull();
        }

        [Fact]
        public void HasCorrectActionAttribute()
        {
            var actionAttribute = Attribute.GetCustomAttribute(typeof(ListBundleAction), typeof(ActionAttribute)) as ActionAttribute;

            actionAttribute.Should().NotBeNull();
            actionAttribute!.Name.Should().Be("list");
            actionAttribute.Context.Should().Be(Context.Bundles);
            actionAttribute.HelpText.Should().Contain("List");
        }

        [Fact]
        public void HasCorrectActionAttribute_HelpTextDescribesDownloadedBundles()
        {
            var actionAttribute = Attribute.GetCustomAttribute(typeof(ListBundleAction), typeof(ActionAttribute)) as ActionAttribute;

            actionAttribute.Should().NotBeNull();
            actionAttribute!.HelpText.Should().Contain("downloaded");
            actionAttribute.HelpText.Should().Contain("extension bundles");
        }

        [Fact]
        public async Task RunAsync_WithoutHostJson_OutputsConfigurationMessage()
        {
            Directory.SetCurrentDirectory(_testDirectory);
            var action = new ListBundleAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("configure extension bundles");
        }

        [Fact]
        public async Task RunAsync_WithHostJsonWithoutBundles_OutputsConfigurationMessage()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, "{\"version\": \"2.0\"}");

            var action = new ListBundleAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("configure extension bundles");
        }

        [Fact]
        public async Task RunAsync_WithBundlesConfigured_NoBundlesDownloaded_OutputsDownloadMessage()
        {
            Directory.SetCurrentDirectory(_testDirectory);
            var tempExtensionBundlePath = Path.Combine(Path.GetTempPath(), "FuncListBundleTest", "bundles");

            var hostJson = Path.Combine(_testDirectory, "host.json");
            var hostJsonContent = @"{
                ""version"": ""2.0"",
                ""extensionBundle"": {
                    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
                    ""version"": ""[4.*, 5.0.0)"",
                    ""downloadPath"": """ + tempExtensionBundlePath.Replace("\\", "\\\\") + @"""
                }
            }";
            await File.WriteAllTextAsync(hostJson, hostJsonContent);

            var action = new ListBundleAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("No bundles");
        }

        [Fact]
        public async Task RunAsync_WithBundlesConfigured_BundleExists_ListsBundle()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            var hostJsonContent = @"{
                ""version"": ""2.0"",
                ""extensionBundle"": {
                    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
                    ""version"": ""[4.*, 5.0.0)"",
                    ""downloadPath"": ""bundles""
                }
            }";
            await File.WriteAllTextAsync(hostJson, hostJsonContent);

            // Create a fake bundle directory with content
            var bundlePath = Path.Combine(_testDirectory, "bundles", "4.30.0");
            Directory.CreateDirectory(bundlePath);
            await File.WriteAllTextAsync(Path.Combine(bundlePath, "bundle.json"), "{}");

            var action = new ListBundleAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("Available extension bundles");
            output.Should().Contain("4.30.0");
            output.Should().Contain("Microsoft.Azure.Functions.ExtensionBundle");
        }

        [Fact]
        public async Task RunAsync_WithMultipleBundleVersions_ListsAllVersions()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            var hostJsonContent = @"{
                ""version"": ""2.0"",
                ""extensionBundle"": {
                    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
                    ""version"": ""[4.*, 5.0.0)"",
                    ""downloadPath"": ""bundles""
                }
            }";
            await File.WriteAllTextAsync(hostJson, hostJsonContent);

            // Create multiple bundle versions
            var bundle1 = Path.Combine(_testDirectory, "bundles", "4.28.0");
            var bundle2 = Path.Combine(_testDirectory, "bundles", "4.30.0");
            Directory.CreateDirectory(bundle1);
            Directory.CreateDirectory(bundle2);
            await File.WriteAllTextAsync(Path.Combine(bundle1, "bundle.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(bundle2, "bundle.json"), "{}");

            var action = new ListBundleAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("4.28.0");
            output.Should().Contain("4.30.0");
        }

        [Fact]
        public async Task RunAsync_WithEmptyBundleDirectory_OutputsNoBundle()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            var hostJsonContent = @"{
                ""version"": ""2.0"",
                ""extensionBundle"": {
                    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
                    ""version"": ""[4.*, 5.0.0)"",
                    ""downloadPath"": ""bundles""
                }
            }";
            await File.WriteAllTextAsync(hostJson, hostJsonContent);

            // Create empty bundle directory (no version subdirectories)
            var bundlePath = Path.Combine(_testDirectory, "bundles");
            Directory.CreateDirectory(bundlePath);

            var action = new ListBundleAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("No bundles found");
        }

        [Fact]
        public async Task RunAsync_ShowsConfiguredVersionRange()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            var hostJsonContent = @"{
                ""version"": ""2.0"",
                ""extensionBundle"": {
                    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
                    ""version"": ""[4.*, 5.0.0)"",
                    ""downloadPath"": ""bundles""
                }
            }";
            await File.WriteAllTextAsync(hostJson, hostJsonContent);

            var bundlePath = Path.Combine(_testDirectory, "bundles", "4.30.0");
            Directory.CreateDirectory(bundlePath);
            await File.WriteAllTextAsync(Path.Combine(bundlePath, "bundle.json"), "{}");

            var action = new ListBundleAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("Configured version range");
            output.Should().Contain("[4.*, 5.0.0)");
        }

        [Fact]
        public void InheritsFromBaseAction()
        {
            var action = new ListBundleAction();

            action.Should().BeAssignableTo<BaseAction>();
        }
    }
}
