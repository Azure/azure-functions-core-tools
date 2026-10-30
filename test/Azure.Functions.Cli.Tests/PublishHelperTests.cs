using Azure.Functions.Cli.Helpers;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Azure.Functions.Cli.PublishHelperTests
{
    public class PublishHelperTests
    {
        [Theory]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/node", false)]
        [InlineData("DOCKER|customimage", true)]
        [InlineData("NODE|18", false)]
        [InlineData("DOTNET", false)]
        [InlineData("", false)]
        public void IsLinuxFxVersionUsingCustomImageTest(string linuxFxVersion, bool expected)
        {
            Assert.Equal(expected, PublishHelper.IsLinuxFxVersionUsingCustomImage(linuxFxVersion));
        }

        [Theory]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/dotnet", WorkerRuntime.dotnet, true)]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/node", WorkerRuntime.dotnet, false)]
        [InlineData("DOCKER|customimage", WorkerRuntime.dotnet, false)]
        [InlineData("NODE|18", WorkerRuntime.node, true)]
        [InlineData("NODE|18", WorkerRuntime.dotnet, false)]
        [InlineData("", WorkerRuntime.dotnet, true)]
        public void IsLinuxFxVersionRuntimeMatchedTest(string linuxFxVersion, WorkerRuntime runtime, bool expected)
        {
            Assert.Equal(expected, PublishHelper.IsLinuxFxVersionRuntimeMatched(linuxFxVersion, runtime));
        }
    }
}
