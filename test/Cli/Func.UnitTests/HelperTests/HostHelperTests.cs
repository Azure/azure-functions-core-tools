// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class HostHelperTests
    {
        [Fact]
        public async Task GetCustomHandlerExecutable_Throws_When_HostJson_Missing()
        {
            // Arrange
            var fs = Substitute.For<IFileSystem>();
            fs.File.Exists(Arg.Any<string>()).Returns(false);

            using (FileSystemHelpers.Override(fs))
            {
                // Act/Assert
                await Assert.ThrowsAsync<InvalidOperationException>(() => HostHelpers.GetCustomHandlerExecutable());
            }
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Returns_ExecutablePath_When_Present()
        {
            // Arrange
            var json = @"{""customHandler"":{""description"":{ ""defaultExecutablePath"":""file.exe"" }}}";
            var fs = Substitute.For<IFileSystem>();

            fs.File.Exists(Arg.Any<string>()).Returns(ci =>
                string.Equals(Path.GetFileName(ci.Arg<string>()), "host.json", StringComparison.OrdinalIgnoreCase));

            Stream HostJsonStream() => new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);

            fs.File.OpenRead(Arg.Any<string>()).Returns(ci =>
            {
                var path = ci.Arg<string>();
                if (string.Equals(Path.GetFileName(path), "host.json", StringComparison.OrdinalIgnoreCase))
                {
                    return HostJsonStream();
                }

                throw new FileNotFoundException(path);
            });

            fs.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
              .Returns(ci =>
              {
                  var path = ci.ArgAt<string>(0);
                  if (string.Equals(Path.GetFileName(path), "host.json", StringComparison.OrdinalIgnoreCase))
                  {
                      return HostJsonStream();
                  }

                  throw new FileNotFoundException(path);
              });

            using (FileSystemHelpers.Override(fs))
            {
                // Act
                var result = await HostHelpers.GetCustomHandlerExecutable();

                // Assert
                result.Should().Be("file.exe");
            }
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Returns_Empty_When_ExecutablePath_Missing()
        {
            // Arrange
            var json = @"{""customHandler"":{ ""description"":{}}}";
            var fs = Substitute.For<IFileSystem>();

            fs.File.Exists(Arg.Any<string>()).Returns(ci =>
                string.Equals(Path.GetFileName(ci.Arg<string>()), "host.json", StringComparison.OrdinalIgnoreCase));

            Stream HostJsonStream() => new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);

            fs.File.OpenRead(Arg.Any<string>()).Returns(ci =>
            {
                var path = ci.Arg<string>();
                if (string.Equals(Path.GetFileName(path), "host.json", StringComparison.OrdinalIgnoreCase))
                {
                    return HostJsonStream();
                }

                throw new FileNotFoundException(path);
            });

            fs.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
              .Returns(ci =>
              {
                  var path = ci.ArgAt<string>(0);
                  if (string.Equals(Path.GetFileName(path), "host.json", StringComparison.OrdinalIgnoreCase))
                  {
                      return HostJsonStream();
                  }

                  throw new FileNotFoundException(path);
              });

            using (FileSystemHelpers.Override(fs))
            {
                // Act
                var result = await HostHelpers.GetCustomHandlerExecutable();

                // Assert
                result.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Returns_Empty_When_CustomHandler_Missing()
        {
            // Arrange
            var json = @"{}";
            var fs = Substitute.For<IFileSystem>();

            fs.File.Exists(Arg.Any<string>()).Returns(ci =>
                string.Equals(Path.GetFileName(ci.Arg<string>()), "host.json", StringComparison.OrdinalIgnoreCase));

            Stream HostJsonStream() => new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);

            fs.File.OpenRead(Arg.Any<string>()).Returns(ci =>
            {
                var path = ci.Arg<string>();
                if (string.Equals(Path.GetFileName(path), "host.json", StringComparison.OrdinalIgnoreCase))
                {
                    return HostJsonStream();
                }

                throw new FileNotFoundException(path);
            });

            fs.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
              .Returns(ci =>
              {
                  var path = ci.ArgAt<string>(0);
                  if (string.Equals(Path.GetFileName(path), "host.json", StringComparison.OrdinalIgnoreCase))
                  {
                      return HostJsonStream();
                  }

                  throw new FileNotFoundException(path);
              });

            using (FileSystemHelpers.Override(fs))
            {
                // Act
                var result = await HostHelpers.GetCustomHandlerExecutable();

                // Assert
                result.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task GetCustomHandlerExecutable_Uses_Provided_Path_To_Read_HostJson()
        {
            // Arrange
            var customRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var expectedHostJsonPath = Path.Combine(customRoot, Constants.HostJsonFileName);
            var json = @"{""customHandler"":{""description"":{ ""defaultExecutablePath"":""file.exe"" }}}";

            var fs = Substitute.For<IFileSystem>();

            fs.File.Exists(Arg.Any<string>())
              .Returns(ci => string.Equals(ci.ArgAt<string>(0), expectedHostJsonPath, StringComparison.OrdinalIgnoreCase));

            Stream HostJsonStream() => new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);

            fs.File.OpenRead(Arg.Any<string>()).Returns(ci =>
            {
                var path = ci.Arg<string>();
                if (string.Equals(path, expectedHostJsonPath, StringComparison.OrdinalIgnoreCase))
                {
                    return HostJsonStream();
                }

                throw new FileNotFoundException(path);
            });

            fs.File.Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
              .Returns(ci =>
              {
                  var path = ci.ArgAt<string>(0);
                  if (string.Equals(path, expectedHostJsonPath, StringComparison.OrdinalIgnoreCase))
                  {
                      return HostJsonStream();
                  }

                  throw new FileNotFoundException(path);
              });

            using (FileSystemHelpers.Override(fs))
            {
                // Act
                var result = await HostHelpers.GetCustomHandlerExecutable(customRoot);

                // Assert
                result.Should().Be("file.exe");
            }
        }
    }
}
