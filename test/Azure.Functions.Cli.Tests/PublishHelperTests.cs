﻿using Azure.Functions.Cli.Helpers;
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
        [InlineData("PYTHON|3.6", false)]
        [InlineData("DOTNET", false)]
        [InlineData("", false)]
        public void IsLinuxFxVersionUsingCustomImageTest(string linuxFxVersion, bool expected)
        {
            Assert.Equal(expected, PublishHelper.IsLinuxFxVersionUsingCustomImage(linuxFxVersion));
        }

        [Theory]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/dotnet", WorkerRuntime.Dotnet, true)]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/node", WorkerRuntime.Dotnet, false)]
        [InlineData("DOCKER|customimage", WorkerRuntime.Dotnet, false)]
        [InlineData("PYTHON|3.7", WorkerRuntime.Python, true)]
        [InlineData("PYTHON|3.7", WorkerRuntime.Node, false)]
        [InlineData("", WorkerRuntime.Dotnet, true)]
        public void IsLinuxFxVersionRuntimeMatchedTest(string linuxFxVersion, WorkerRuntime runtime, bool expected)
        {
            Assert.Equal(expected, PublishHelper.IsLinuxFxVersionRuntimeMatched(linuxFxVersion, runtime));
        }
    }
}
