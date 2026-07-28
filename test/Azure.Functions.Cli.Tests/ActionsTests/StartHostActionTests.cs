using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class StartHostActionTests : IDisposable
    {
        [SkippableFact]
        public async Task CheckNonOptionalSettingsThrowsOnMissingAzureWebJobsStorageAndManagedIdentity()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");

            var fileSystem = GetFakeFileSystem(new[]
            {
                ("x:\\folder1", "{'bindings': [{'type': 'blobTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            });

            FileSystemHelpers.Instance = fileSystem;

            Exception exception = null;
            try
            {
                await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), "x:\\", false);
            }
            catch (Exception e)
            {
                exception = e;
            }

            exception.Should().NotBeNull();
            exception.Should().BeOfType<CliException>();
            exception.Message.Should().Contain($"Missing value for AzureWebJobsStorage in local.settings.json. " +
                $"This is required for all triggers other than {string.Join(", ", Constants.TriggersWithoutStorage)}.");
        }

        [Fact]
        public async Task CheckNonOptionalSettingsDoesntThrowMissingConnectionUsingManagedIdentity()
        {
            var fileSystem = GetFakeFileSystem(new[]
                {
                ("x:\\folder1", "{'bindings': [{'type': 'serviceBusTrigger', 'connection': 'myServiceBusConnection'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'eventHubTrigger', 'connection': 'myEventHubConnection'}]}"),
                ("x:\\folder3", "{'bindings': [{'type': 'blobTrigger', 'connection': 'myBlobStorage'}]}"),
                ("x:\\folder4", "{'bindings': [{'type': 'queueTrigger', 'connection': 'myQueueStorage'}]}")
            });

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage", "myuri" },
                { "myServiceBusConnection", "myuri" },
                { "myEventHubConnection__fullyQualifiedNamespace", "myEHNamespace.servicebus.windows.net" },
                { "myBlobStorage__blobServiceUri", "myuri" },
                { "myQueueStorage__accountName", "myAccountName" }
            };

            FileSystemHelpers.Instance = fileSystem;

            Exception exception = null;
            try
            {
                await StartHostAction.CheckNonOptionalSettings(secrets, "x:\\");
            }
            catch (Exception e)
            {
                exception = e;
            }
            exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckNonOptionalSettingsDoesntThrowMissingStorageUsingManagedIdentity()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");
            var fileSystem = GetFakeFileSystem(new[]
                {
                ("x:\\folder1", "{'bindings': [{'type': 'blobTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            });

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage:blobServiceUri", "myuri" },
                { "AzureWebJobsStorage__queueServiceUri", "queueuri" }
            };

            FileSystemHelpers.Instance = fileSystem;

            Exception exception = null;
            try
            {
                await StartHostAction.CheckNonOptionalSettings(secrets, "x:\\");
            }
            catch (Exception e)
            {
                exception = e;
            }
            exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckNonOptionalSettingsDoesntThrowOnMissingAzureWebJobsStorage()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");
            var fileSystem = GetFakeFileSystem(new[]
                {
                ("x:\\folder1", "{'bindings': [{'type': 'blobTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            });

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage:blobServiceUri", "myuri" },
                { "AzureWebJobsStorage__queueServiceUri", "queueuri" }
            };

            FileSystemHelpers.Instance = fileSystem;

            Exception exception = null;
            try
            {
                await StartHostAction.CheckNonOptionalSettings(secrets, "x:\\", false);
            }
            catch (Exception e)
            {
                exception = e;
            }
            exception.Should().BeNull();
        }

        [SkippableFact]
        public async Task CheckNonOptionalSettingsPrintsWarningForMissingSettings()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");

            var fileSystem = GetFakeFileSystem(new[]
            {
                ("x:\\folder1", "{'bindings': [{'type': 'httpTrigger', 'connection': 'blah'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger', 'connection': ''}]}")
            });

            FileSystemHelpers.Instance = fileSystem;

            var output = new StringBuilder();
            var console = Substitute.For<IConsoleWriter>();
            console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => output.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;
            ColoredConsole.Error = console;

            await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), "x:\\", false);
            output.ToString().Should().Contain("Warning: Cannot find value named 'blah'");
            output.ToString().Should().Contain("Warning: 'connection' property in 'x:\\folder2\\function.json' is empty.");
        }

        [Fact]
        public void ParseArgs_NoAddressAndNoConfig_DefaultsToLoopback()
        {
            var action = CreateStartHostAction(new HostStartSettings());

            action.ParseArgs(Array.Empty<string>());

            action.Address.Should().Be("127.0.0.1");
            action.ResolveBindAddress().Should().Be(IPAddress.Loopback);
        }

        [Fact]
        public void ParseArgs_ExplicitAddress_OverridesDefault()
        {
            var action = CreateStartHostAction(new HostStartSettings());

            action.ParseArgs(new[] { "--address", "0.0.0.0" });

            action.Address.Should().Be("0.0.0.0");
            action.ResolveBindAddress().Should().Be(IPAddress.Any);
        }

        [Fact]
        public void ParseArgs_ConfigAddress_UsedWhenNoFlag()
        {
            var action = CreateStartHostAction(new HostStartSettings { LocalHttpAddress = "0.0.0.0" });

            action.ParseArgs(Array.Empty<string>());

            action.Address.Should().Be("0.0.0.0");
        }

        [Fact]
        public void ResolveBindAddress_InvalidAddress_ThrowsCliException()
        {
            var action = CreateStartHostAction(new HostStartSettings());
            action.Address = "not-an-ip";

            Action resolve = () => action.ResolveBindAddress();

            resolve.Should().Throw<CliException>();
        }

        private static StartHostAction CreateStartHostAction(HostStartSettings hostStartSettings)
        {
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetHostStartSettings().Returns(hostStartSettings);
            var processManager = Substitute.For<IProcessManager>();
            return new StartHostAction(secretsManager, processManager);
        }

        private IFileSystem GetFakeFileSystem(IEnumerable<(string folder, string functionJsonContent)> list)
        {
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.Directory.GetDirectories(Arg.Any<string>()).Returns(list.Select(t => t.folder).ToArray());
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);

            foreach ((var folder, var fileContent) in list)
            {
                //fileSystem.File.Exists(Arg.Is(Path.Combine(folder, "function.json"))).Returns(true);

                fileSystem.File.Open(Arg.Is(Path.Combine(folder, "function.json")), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                    .Returns(fileContent.ToStream());
            }

            return fileSystem;
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = null;
        }
    }
}
