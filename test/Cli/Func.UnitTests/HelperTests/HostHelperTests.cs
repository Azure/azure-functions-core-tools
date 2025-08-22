// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class HostHelperTests : IDisposable
    {
        private readonly IFileSystem _originalFileSystem;

        public HostHelperTests()
        {
            _originalFileSystem = FileSystemHelpers.Instance;
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Throws_When_HostJson_Missing()
        {
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(false);
            FileSystemHelpers.Instance = fileSystem;

            await Assert.ThrowsAsync<InvalidOperationException>(() => HostHelpers.GetCustomHandlerExecutable());
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Returns_ExecutablePath_When_Present()
        {
            var json = @"{""customHandler"":{""description"":{ ""defaultExecutablePath"":""file.exe"" }}}";
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            fileSystem.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(ci =>
                {
                    var path = ci.ArgAt<string>(0);
                    if (Path.GetFileName(path) == "host.json")
                    {
                        return json.ToStream();
                    }

                    throw new FileNotFoundException(path);
                });

            FileSystemHelpers.Instance = fileSystem;

            var result = await HostHelpers.GetCustomHandlerExecutable();
            result.Should().Be("file.exe");
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Returns_Empty_When_ExecutablePath_Missing()
        {
            var json = @"{""customHandler"":{ ""description"":{}}}";
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            fileSystem.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(ci =>
                {
                    var path = ci.ArgAt<string>(0);
                    if (Path.GetFileName(path) == "host.json")
                    {
                        return json.ToStream();
                    }

                    throw new FileNotFoundException(path);
                });

            FileSystemHelpers.Instance = fileSystem;

            var result = await HostHelpers.GetCustomHandlerExecutable();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Returns_Empty_When_CustomHandler_Missing()
        {
            var json = @"{}";
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            fileSystem.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(ci =>
                {
                    var path = ci.ArgAt<string>(0);
                    if (Path.GetFileName(path) == "host.json")
                    {
                        return json.ToStream();
                    }

                    throw new FileNotFoundException(path);
                });

            FileSystemHelpers.Instance = fileSystem;

            var result = await HostHelpers.GetCustomHandlerExecutable();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Uses_Provided_Path_To_Read_HostJson()
        {
            // Arrange
            var customRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var expectedHostJsonPath = Path.Combine(customRoot, Constants.HostJsonFileName);
            var json = @"{""customHandler"":{""description"":{ ""defaultExecutablePath"":""file.exe"" }}}";

            var fileSystem = Substitute.For<IFileSystem>();

            fileSystem.File.Exists(Arg.Any<string>())
                .Returns(ci => string.Equals(ci.ArgAt<string>(0), expectedHostJsonPath, StringComparison.OrdinalIgnoreCase));

            fileSystem.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(ci =>
                {
                    var path = ci.ArgAt<string>(0);
                    if (string.Equals(path, expectedHostJsonPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return json.ToStream();
                    }

                    throw new FileNotFoundException(path);
                });

            FileSystemHelpers.Instance = fileSystem;

            // Act
            var result = await HostHelpers.GetCustomHandlerExecutable(customRoot);

            // Assert
            result.Should().Be("file.exe");
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = _originalFileSystem;
        }
    }
}
