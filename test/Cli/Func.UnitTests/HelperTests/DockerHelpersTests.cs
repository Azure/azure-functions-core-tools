// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DockerHelpersTests
    {
        [Fact]
        public void GetDockerRunArguments_InheritsEnvironmentVariablesWithoutIncludingValues()
        {
            var args = DockerHelpers.GetDockerRunArguments(
                "example/image:latest",
                command: "sleep infinity",
                environmentVariables: ["PIP_INDEX_URL", "PIP_EXTRA_INDEX_URL"]);

            Assert.Equal(
                "run --rm -d -it --env PIP_INDEX_URL --env PIP_EXTRA_INDEX_URL example/image:latest sleep infinity",
                args);
        }

        [Fact]
        public void GetDockerRunArguments_OmitsEmptyOptionalArguments()
        {
            var args = DockerHelpers.GetDockerRunArguments("example/image:latest");

            Assert.Equal("run --rm -d -it example/image:latest", args);
        }
    }
}
