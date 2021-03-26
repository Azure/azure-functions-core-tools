
using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class HostHelperTests
    {
        [Theory]
        [InlineData("{'customHandler':{'description':{'defaultExecutablePath':'file.exe'}}}", false, "file.exe")]
        [InlineData("{'customHandler':{'description':{}}}", false, "")]
        [InlineData("{}", false, "")]
        [InlineData(null, true, "")]
        public async Task GetCustomHandlerExecutableTest(string hostJsonContent, bool exception, string expected)
        {
            var fileSystem = Substitute.For<IFileSystem>();
            if (hostJsonContent != null)
            {
                fileSystem.File.Exists(Arg.Any<string>()).Returns(true);

                fileSystem.File.Open(Arg.Is("host.json"),
                                     Arg.Any<FileMode>(),
                                     Arg.Any<FileAccess>(),
                                     Arg.Any<FileShare>())
                    .Returns(hostJsonContent.ToStream());
            }

            FileSystemHelpers.Instance = fileSystem;

            Func<Task<string>> action = () => HostHelpers.GetCustomHandlerExecutable();

            if (exception)
            {
                action.Should().Throw<InvalidOperationException>();
            }
            else
            {
                var result = await action();
                result.Should().Be(expected);
            }
        }
    }
}