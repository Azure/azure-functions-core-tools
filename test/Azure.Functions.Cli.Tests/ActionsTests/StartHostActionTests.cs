﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class StartHostActionTests : IDisposable
    {
        [SkippableFact]
        public async Task CheckNonOptionalSettingsThrowsOnMissingAzureWebJobsStorage()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failling");

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
        public async Task CheckIfProjectIsAlreadyBuilt()
        {
            var fileSystem = GetFakeFileSystem(new[]
            {
                (Path.Combine("x:", "folder1"), "{'scriptFile': '../bin/blah.dll'}"),
                (Path.Combine("x:", "folder2"), "{'bindings': [{'type': 'httpTrigger'}]}")
            });
            fileSystem.File.Exists(Arg.Is(Path.Combine("x:", "folder1", "../bin/blah.dll"))).Returns(true);
            FileSystemHelpers.Instance = fileSystem;
            bool test = await StartHostAction.HasBeenBuilt(".");
            Assert.True(test);

            fileSystem.File.Exists(Arg.Is(Path.Combine("x:", "folder1", "../bin/blah.dll"))).Returns(false);
            Assert.False(await StartHostAction.HasBeenBuilt("."));
        }

        [SkippableFact]
        public async Task CheckNonOptionalSettingsPrintsWarningForMissingSettings()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failling");

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

        public void Dispose()
        {
            FileSystemHelpers.Instance = null;
        }
    }
}
