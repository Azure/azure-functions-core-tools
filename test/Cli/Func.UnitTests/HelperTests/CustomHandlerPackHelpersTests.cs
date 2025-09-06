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
    public class CustomHandlerPackHelpersTests : IDisposable
    {
        private readonly IFileSystem _originalFileSystem;

        public CustomHandlerPackHelpersTests()
        {
            _originalFileSystem = FileSystemHelpers.Instance;
        }

        [Fact]
        public async Task GetCustomHandlerExecutablesAsync_Returns_Empty_When_No_CustomHandler()
        {
            // Arrange
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

            // Act
            var result = await CustomHandlerPackHelpers.GetCustomHandlerExecutablesAsync("/test/path");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCustomHandlerExecutablesAsync_Returns_Executable_When_CustomHandler_Present()
        {
            // Arrange
            var json = @"{""customHandler"":{""description"":{ ""defaultExecutablePath"":""my-handler.exe"" }}}";
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

            // Act
            var result = await CustomHandlerPackHelpers.GetCustomHandlerExecutablesAsync("/test/path");

            // Assert
            result.Should().ContainSingle().Which.Should().Be("my-handler.exe");
        }

        [Fact]
        public async Task GetCustomHandlerExecutablesAsync_Returns_Empty_When_HostJson_Missing()
        {
            // Arrange
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(false);
            FileSystemHelpers.Instance = fileSystem;

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                CustomHandlerPackHelpers.GetCustomHandlerExecutablesAsync("/test/path"));
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = _originalFileSystem;
        }
    }
}