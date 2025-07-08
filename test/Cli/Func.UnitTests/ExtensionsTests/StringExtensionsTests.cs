// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Exceptions;
using Azure.Functions.Cli.Extensions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ExtensionsTests
{
    public class StringExtensionsTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void TestSanitizeImageNameWithBadInputValueThrowArgumentException(string input)
        {
            // A
            var exception = Assert.Throws<ArgumentException>(() => input.SanitizeImageName());

            // A
            Assert.NotNull(exception);
            Assert.NotNull(exception.Message);
            Assert.Contains("imageName cannot be null or empty", exception.Message);
        }

        [Theory]
        [InlineData("GetMessages", "getmessages")]
        [InlineData("hello_World", "hello_world")]
        [InlineData("hello-World", "helloworld")]
        [InlineData("#So-Pretty !/^Good|'Name{}", "soprettygoodname")]
        public void TestSanitizeImageNameWithBadFormatSanitizeOutput(string input, string expected)
        {
            // A
            var imageNameSanitized = input.SanitizeImageName();

            // A
            Assert.NotNull(imageNameSanitized);
            Assert.Equal(expected, imageNameSanitized);
        }

        [Fact]
        public void TestSanitizeImageNameWithBadFormatThrowImageNameFormatException()
        {
            // A
            string imageName = "#!^{}";

            // A
            var exception = Assert.Throws<ImageNameFormatException>(() => imageName.SanitizeImageName());

            // A
            Assert.NotNull(exception);
            Assert.NotNull(exception.Message);
            Assert.Contains($"{imageName} cannot be converted in a good image format", exception.Message);
        }
    }
}
