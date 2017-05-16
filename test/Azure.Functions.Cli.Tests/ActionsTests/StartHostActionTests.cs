using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Common;
using Colors.Net;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class StartHostActionTests
    {
        [Fact]
        public async Task CheckNonOptionalSettingsThrowsOnMissingAzureWebJobsStorage()
        {
            var fileSystem = GetFakeFileSystem(new[]
            {
                ("x:\\folder1", "{'bindings': [{'type': 'blobTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            });

            FileSystemHelpers.Instance = fileSystem;

            Exception exception = null;
            try
            {
                await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), "x:\\");
            }
            catch (Exception e)
            {
                exception = e;
            }

            exception.Should().NotBeNull();
            exception.Should().BeOfType<CliException>();
            exception.Message.Should().Contain("Missing value for AzureWebJobsStorage in local.settings.json. This is required for all triggers other than HTTP.");
        }

        [Fact]
        public async Task CheckNonOptionalSettingsDoesntThrowOnMissingAzureWebJobsStorage()
        {
            var fileSystem = GetFakeFileSystem(new[]
            {
                ("x:\\folder1", "{'bindings': [{'type': 'httpTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            });

            FileSystemHelpers.Instance = fileSystem;

            Exception exception = null;
            try
            {
                await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), "x:\\");
            }
            catch (Exception e)
            {
                exception = e;
            }

            exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckNonOptionalSettingsPrintsWarningForMissingSettings()
        {
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

            await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), "x:\\");
            output.ToString().Should().Contain("Warning: Cannot find value named 'blah'");
            output.ToString().Should().Contain("Warning: 'connection' property in 'x:\\folder2\\function.json' is empty.");
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
    }
}
