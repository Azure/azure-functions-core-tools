// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class StacksApiHelperTests
    {
        [Theory]
        [InlineData("net8.0", 8)]
        [InlineData("net9.0", 9)]
        [InlineData("net10.0", 10)]
        [InlineData("net48", 48)]
        public void GetMajorDotnetVersionFromDotnetVersionInProject_ReturnsMajorVersion(string input, int expected)
        {
            var result = StacksApiHelper.GetMajorDotnetVersionFromDotnetVersionInProject(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid")]
        public void GetMajorDotnetVersionFromDotnetVersionInProject_ReturnsNull_ForInvalidInput(string input)
        {
            var result = StacksApiHelper.GetMajorDotnetVersionFromDotnetVersionInProject(input);

            result.Should().BeNull();
        }
    }
}
