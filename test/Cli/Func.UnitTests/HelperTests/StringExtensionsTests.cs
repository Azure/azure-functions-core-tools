// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Extensions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class StringExtensionsTests
    {
        [Theory]
        [InlineData(null, "InstrumentationKey", ';', null)]
        [InlineData("", "InstrumentationKey", ';', null)]
        [InlineData(";", "InstrumentationKey", ';', null)]
        [InlineData("InstrumentationKey=abc123;", "InstrumentationKey", ';', "abc123")]
        [InlineData("InstrumentationKey=abc123;IngestionEndpoint=https://...", "InstrumentationKey", ';', "abc123")]
        [InlineData("  InstrumentationKey  =   abc123    ;", "InstrumentationKey", ';', "abc123")]
        [InlineData("InstrumentationKey=abc123;OtherKey=xyz", "InstrumentationKey", ';', "abc123")]
        [InlineData("otherKey=xyz;InstrumentationKey=abc123;", "InstrumentationKey", ';', "abc123")]
        [InlineData("otherKey=xyz;InstrumentationKey=abc123", "InstrumentationKey", ';', "abc123")]
        [InlineData("InstrumentationKey=ABC123", "InstrumentationKey", ';', "ABC123")]
        [InlineData("instrumentationkey=abc123", "InstrumentationKey", ';', "abc123")]
        [InlineData("InstrumentationKey= ;", "InstrumentationKey", ';', null)]
        [InlineData("SomeKey=SomeValue;AnotherKey=AnotherValue", "InstrumentationKey", ';', null)]
        public void ExtractIKeyFromConnectionString_ReturnsExpectedInstrumentationKey(string connectionString, string key, char delimiter, string expected)
        {
            var actual = connectionString.GetValueFromDelimitedString(key, delimiter);
            Assert.Equal(expected, actual);
        }
    }
}
