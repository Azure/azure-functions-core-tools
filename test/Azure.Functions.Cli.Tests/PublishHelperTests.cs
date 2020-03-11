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
        [InlineData("mcr.microsoft.com/azure-functions/dotnet", true)]
        [InlineData("mcr.microsoft.com/azure-functions/node10", true)]
        [InlineData("mcr.microsoft.com/azure-functions/invalid", false)]
        [InlineData("PYTHON|3.6", true)]
        [InlineData("NODE|8", true)]
        [InlineData("DOTNET|34", false)]
        [InlineData("DOTNET", false)]
        [InlineData("", false)]
        public void IsLinuxFxVersionValidTest(string linuxFxVersion, bool expected)
        {
            Assert.Equal(expected, PublishHelper.IsLinuxFxVersionValid(linuxFxVersion)); 
        }

        [Theory]
        [InlineData("mcr.microsoft.com/azure-functions/dotnet", WorkerRuntime.dotnet, true)]
        [InlineData("mcr.microsoft.com/azure-functions/node", WorkerRuntime.dotnet, false)]
        [InlineData("mcr.microsoft.com/azure-functions/invalid", WorkerRuntime.dotnet, false)]
        [InlineData("PYTHON|3.6", WorkerRuntime.python, true)]
        [InlineData("NODE|8", WorkerRuntime.node, true)]
        [InlineData("NODE|10", WorkerRuntime.None, false)]
        [InlineData("JAVA|8", WorkerRuntime.java, false)]
        [InlineData("DOTNET|3", WorkerRuntime.python, false)]
        [InlineData("DOTNET", WorkerRuntime.dotnet, false)]
        [InlineData("", WorkerRuntime.dotnet, false)]
        [InlineData("", WorkerRuntime.None, false)]
        public void DoesLinuxFxVersionMatchRuntimeTest(string linuxFxVersion, WorkerRuntime runtime, bool expected)
        {
            Assert.Equal(expected, PublishHelper.DoesLinuxFxVersionMatchRuntime(linuxFxVersion, runtime));
        }
    }
}
