// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class EnvironmentHelperTests : IDisposable
    {
        private const string TestKey = "FUNC_CLI_ENV_HELPER_TEST_KEY";
        private readonly string _previousValue;

        public EnvironmentHelperTests()
        {
            _previousValue = Environment.GetEnvironmentVariable(TestKey);
            Environment.SetEnvironmentVariable(TestKey, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(TestKey, _previousValue);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("True", true)]
        [InlineData("TRUE", true)]
        [InlineData("1", true)]
        [InlineData("false", false)]
        [InlineData("False", false)]
        [InlineData("FALSE", false)]
        [InlineData("0", false)]
        public void GetEnvironmentVariableAsNullableBool_ParsesKnownValues(string value, bool expected)
        {
            Environment.SetEnvironmentVariable(TestKey, value);

            EnvironmentHelper.GetEnvironmentVariableAsNullableBool(TestKey).Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("yes")]
        [InlineData("no")]
        [InlineData("garbage")]
        public void GetEnvironmentVariableAsNullableBool_ReturnsNullForUnsetOrUnknown(string value)
        {
            Environment.SetEnvironmentVariable(TestKey, value);

            EnvironmentHelper.GetEnvironmentVariableAsNullableBool(TestKey).Should().BeNull();
        }
    }
}
