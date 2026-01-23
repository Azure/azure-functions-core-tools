// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Colors.Net;
using FluentAssertions;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    [Collection("BundleActionTests")]
    public class AddBundleActionTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string _originalDirectory;
        private readonly StringBuilder _consoleOutput;
        private readonly IConsoleWriter _mockConsole;

        public AddBundleActionTests()
        {
            _originalDirectory = Environment.CurrentDirectory;
            _testDirectory = Path.Combine(Path.GetTempPath(), "AddBundleActionTests_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
            Environment.CurrentDirectory = _testDirectory;

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
                Environment.CurrentDirectory = _originalDirectory;
            }
            catch
            {
                // Ignore directory errors, we'll clean up anyway
            }

            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
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
            var action = new AddBundleAction();
            var args = Array.Empty<string>();

            var result = action.ParseArgs(args);

            result.Should().NotBeNull();
            action.Force.Should().BeFalse();
        }

        [Fact]
        public void ParseArgs_WithForceFlag_SetsForceTrue()
        {
            var action = new AddBundleAction();
            var args = new[] { "--force" };

            action.ParseArgs(args);

            action.Force.Should().BeTrue();
        }

        [Fact]
        public void ParseArgs_WithShortForceFlag_SetsForceTrue()
        {
            var action = new AddBundleAction();
            var args = new[] { "-f" };

            action.ParseArgs(args);

            action.Force.Should().BeTrue();
        }

        [Fact]
        public void HasCorrectActionAttribute()
        {
            var actionAttribute = Attribute.GetCustomAttribute(typeof(AddBundleAction), typeof(ActionAttribute)) as ActionAttribute;

            actionAttribute.Should().NotBeNull();
            actionAttribute!.Name.Should().Be("add");
            actionAttribute.Context.Should().Be(Context.Bundles);
            actionAttribute.HelpText.Should().Contain("Add");
        }

        [Fact]
        public async Task RunAsync_WithoutHostJson_ThrowsCliException()
        {
            var action = new AddBundleAction();

            var exception = await Assert.ThrowsAsync<CliException>(() => action.RunAsync());

            exception.Message.Should().Contain(ScriptConstants.HostMetadataFileName);
        }

        [Fact]
        public async Task RunAsync_AddsConfiguration_WhenMissing()
        {
            var hostPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            await File.WriteAllTextAsync(hostPath, "{}");

            var action = new AddBundleAction();
            await action.RunAsync();

            var updated = await File.ReadAllTextAsync(hostPath);
            updated.Should().Contain(Constants.ExtensionBundleConfigPropertyName);
            updated.Should().Contain("Microsoft.Azure.Functions.ExtensionBundle");
        }

        [Fact]
        public async Task RunAsync_AddsConfiguration_PreservesExistingHostJsonContent()
        {
            var hostPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            await File.WriteAllTextAsync(hostPath, @"{""version"": ""2.0"", ""logging"": {""logLevel"": {""default"": ""Information""}}}");

            var action = new AddBundleAction();
            await action.RunAsync();

            var updated = await File.ReadAllTextAsync(hostPath);
            var hostJson = JObject.Parse(updated);

            hostJson["version"]?.ToString().Should().Be("2.0");
            hostJson["logging"].Should().NotBeNull();
            hostJson[Constants.ExtensionBundleConfigPropertyName].Should().NotBeNull();
        }

        [Fact]
        public async Task RunAsync_WithExistingBundle_DoesNotOverwrite_WhenForceNotSet()
        {
            var hostPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            var existingConfig = @"{""extensionBundle"": {""id"": ""Custom.Bundle"", ""version"": ""[1.0.0, 2.0.0)""}}";
            await File.WriteAllTextAsync(hostPath, existingConfig);

            var action = new AddBundleAction();
            await action.RunAsync();

            var updated = await File.ReadAllTextAsync(hostPath);
            updated.Should().Contain("Custom.Bundle");
            updated.Should().NotContain("Microsoft.Azure.Functions.ExtensionBundle");

            var output = _consoleOutput.ToString();
            output.Should().Contain("already configured");
            output.Should().Contain("--force");
        }

        [Fact]
        public async Task RunAsync_WithExistingBundle_Overwrites_WhenForceSet()
        {
            var hostPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            var existingConfig = @"{""extensionBundle"": {""id"": ""Custom.Bundle"", ""version"": ""[1.0.0, 2.0.0)""}}";
            await File.WriteAllTextAsync(hostPath, existingConfig);

            var action = new AddBundleAction { Force = true };
            await action.RunAsync();

            var updated = await File.ReadAllTextAsync(hostPath);
            updated.Should().NotContain("Custom.Bundle");
            updated.Should().Contain("Microsoft.Azure.Functions.ExtensionBundle");
        }

        [Fact]
        public async Task RunAsync_OutputsSuccessMessage()
        {
            var hostPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            await File.WriteAllTextAsync(hostPath, "{}");

            var action = new AddBundleAction();
            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("Extension bundle configuration added");
            output.Should().Contain(ScriptConstants.HostMetadataFileName);
        }

        [Fact]
        public async Task RunAsync_OutputsConfigurationDetails()
        {
            var hostPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            await File.WriteAllTextAsync(hostPath, "{}");

            var action = new AddBundleAction();
            await action.RunAsync();

            var output = _consoleOutput.ToString();
            output.Should().Contain("Microsoft.Azure.Functions.ExtensionBundle");
        }

        [Fact]
        public async Task RunAsync_CreatesValidJson()
        {
            var hostPath = Path.Combine(_testDirectory, ScriptConstants.HostMetadataFileName);
            await File.WriteAllTextAsync(hostPath, @"{""version"": ""2.0""}");

            var action = new AddBundleAction();
            await action.RunAsync();

            var updated = await File.ReadAllTextAsync(hostPath);

            // Should parse without throwing
            var hostJson = JObject.Parse(updated);
            hostJson.Should().NotBeNull();
            hostJson[Constants.ExtensionBundleConfigPropertyName]["id"]?.ToString().Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
        }

        [Fact]
        public void InheritsFromBaseAction()
        {
            var action = new AddBundleAction();

            action.Should().BeAssignableTo<BaseAction>();
        }
    }
}
