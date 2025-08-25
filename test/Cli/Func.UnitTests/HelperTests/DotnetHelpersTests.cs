// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DotnetHelpersTests
    {
        [Theory]
        [InlineData("BlobTrigger", "blob")]
        [InlineData("HttpTrigger", "http")]
        [InlineData("TimerTrigger", "timer")]
        [InlineData("UnknownTrigger", null)]
        public void GetTemplateShortName_ReturnsExpectedShortName(string input, string expected)
        {
            if (expected != null)
            {
                var result = DotnetHelpers.GetTemplateShortName(input);
                Assert.Equal(expected, result);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => DotnetHelpers.GetTemplateShortName(input));
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, 18)]
        [InlineData(WorkerRuntime.DotnetIsolated, 13)]
        public void GetTemplates_ReturnsExpectedTemplates(WorkerRuntime runtime, int expectedCount)
        {
            var templates = DotnetHelpers.GetTemplates(runtime);
            Assert.Equal(expectedCount, templates.Count());
        }
    }
}
