// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
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

        [Theory]
        [InlineData(BuildOption.Default, WorkerRuntime.Python, false, false, BuildOption.Default)] // Python but no build deps and no noBuild
        [InlineData(BuildOption.Default, WorkerRuntime.Node, false, false, BuildOption.Default)] // Non-Python runtime
        [InlineData(BuildOption.Local, WorkerRuntime.Python, false, false, BuildOption.Local)] // Explicit local build option
        [InlineData(BuildOption.Remote, WorkerRuntime.Python, false, false, BuildOption.Remote)] // Explicit remote build option
        [InlineData(BuildOption.Default, WorkerRuntime.Python, true, false, BuildOption.Container)] // Build native deps
        [InlineData(BuildOption.Default, WorkerRuntime.Python, false, true, BuildOption.None)] // No build
        public void ResolveBuildOptionTest(BuildOption inputOption, WorkerRuntime runtime, bool buildNativeDeps, bool noBuild, BuildOption expected)
        {
            // Note: This test validates the basic logic but cannot test requirements.txt scenarios
            // since it would require file system setup. The file system dependent logic is tested separately.
            var result = PublishHelper.ResolveBuildOption(inputOption, runtime, site: null, buildNativeDeps, noBuild);
            Assert.Equal(expected, result);
        }
    }
}
