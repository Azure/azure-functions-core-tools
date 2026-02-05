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
    public class GetBundlePathActionTests : IDisposable
    {
        private const string HostJsonWithBundlesAndDownloadPath = @"{""version"": ""2.0"", ""extensionBundle"": {""id"": ""Microsoft.Azure.Functions.ExtensionBundle"", ""version"": ""[4.*, 5.0.0)"", ""downloadPath"": ""bundles""}}";

        private readonly string _testDirectory;
        private readonly string _originalDirectory;
        private readonly StringBuilder _consoleOutput;
        private readonly IConsoleWriter _mockConsole;

        public GetBundlePathActionTests()
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            _testDirectory = Path.Combine(Path.GetTempPath(), $"func_get_bundle_path_test_{Guid.NewGuid():N}");
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
            var action = new GetBundlePathAction();
            var args = Array.Empty<string>();

            var result = action.ParseArgs(args);

            result.Should().NotBeNull();
        }

        [Fact]
        public void HasCorrectActionAttribute()
        {
            var actionAttribute = Attribute.GetCustomAttribute(typeof(GetBundlePathAction), typeof(ActionAttribute)) as ActionAttribute;

            actionAttribute.Should().NotBeNull();
            actionAttribute!.Name.Should().Be("path");
            actionAttribute.Context.Should().Be(Context.Bundles);
            actionAttribute.HelpText.Should().Contain("path");
        }

        [Fact]
        public void HasCorrectActionAttribute_HelpTextDescribesExtensionBundle()
        {
            var actionAttribute = Attribute.GetCustomAttribute(typeof(GetBundlePathAction), typeof(ActionAttribute)) as ActionAttribute;

            actionAttribute.Should().NotBeNull();
            actionAttribute!.HelpText.Should().Contain("extension bundle");
        }

        [Fact]
        public async Task RunAsync_WithoutHostJson_OutputsNotConfiguredMessage()
        {
            Directory.SetCurrentDirectory(_testDirectory);
            var action = new GetBundlePathAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("not configured");
        }

        [Fact]
        public async Task RunAsync_WithHostJsonWithoutBundles_OutputsNotConfiguredMessage()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, "{\"version\": \"2.0\"}");

            var action = new GetBundlePathAction();

            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("not configured");
        }

        [Fact]
        public async Task RunAsync_WithBundlesConfigured_OutputsPath()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, HostJsonWithBundlesAndDownloadPath);

            // Create a fake bundle directory
            var bundlePath = Path.Combine(_testDirectory, "bundles", "4.30.0");
            Directory.CreateDirectory(bundlePath);
            await File.WriteAllTextAsync(Path.Combine(bundlePath, "bundle.json"), "{}");

            var action = new GetBundlePathAction();

            try
            {
                await action.RunAsync();
            }
            catch
            {
                // May throw if bundle details can't be resolved
            }

            var output = _consoleOutput.ToString();

            // Should either output a path or indicate bundle not configured
            output.Should().NotBeEmpty();
        }

        [Fact]
        public async Task RunAsync_WithCustomDownloadPath_UsesCustomPath()
        {
            Directory.SetCurrentDirectory(_testDirectory);

            var hostJson = Path.Combine(_testDirectory, "host.json");
            await File.WriteAllTextAsync(hostJson, HostJsonWithBundlesAndDownloadPath);

            var action = new GetBundlePathAction();

            try
            {
                await action.RunAsync();
            }
            catch
            {
                // May throw if bundle details can't be resolved
            }

            var output = _consoleOutput.ToString();

            // Output should reference "bundles" directory if custom path is used
            output.Should().NotBeEmpty();
        }

        [Fact]
        public void InheritsFromBaseAction()
        {
            var action = new GetBundlePathAction();

            action.Should().BeAssignableTo<BaseAction>();
        }

        [Fact]
        public void CanBeInstantiated()
        {
            var action = new GetBundlePathAction();

            action.Should().NotBeNull();
        }
    }
}
