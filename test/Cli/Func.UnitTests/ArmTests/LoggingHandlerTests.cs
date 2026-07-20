// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Arm;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ArmTests
{
    public class LoggingHandlerTests
    {
        [Fact]
        public void GetRedactedRequestString_RedactsAuthorizationHeader()
        {
            var input = "Method: GET, RequestUri: 'https://management.azure.com/subscriptions', Version: 1.1, Content: <null>, Headers:\r\n{\r\n  Authorization: Bearer my-secret-token\r\n}";

            var result = LoggingHandler.GetRedactedRequestString(input);

            result.Should().Contain("Authorization: [REDACTED]");
            result.Should().NotContain("my-secret-token");
        }

        [Fact]
        public void GetRedactedRequestString_PreservesNonSensitiveHeaders()
        {
            var input = "Method: GET, RequestUri: 'https://management.azure.com/subscriptions', Version: 1.1, Content: <null>, Headers:\r\n{\r\n  Authorization: Bearer my-secret-token\r\n  Accept: application/json\r\n}";

            var result = LoggingHandler.GetRedactedRequestString(input);

            result.Should().Contain("Accept: application/json");
        }

        [Fact]
        public void GetRedactedRequestString_WithNoAuthorizationHeader_ReturnsInputUnchanged()
        {
            var input = "Method: GET, RequestUri: 'https://management.azure.com/subscriptions', Version: 1.1, Content: <null>, Headers:\r\n{\r\n  Accept: application/json\r\n}";

            var result = LoggingHandler.GetRedactedRequestString(input);

            result.Should().Be(input);
        }

        [Theory]
        [InlineData("authorization: Bearer my-secret-token")]
        [InlineData("AUTHORIZATION: Bearer my-secret-token")]
        [InlineData("Authorization: Bearer my-secret-token")]
        public void GetRedactedRequestString_RedactsAuthorizationHeader_CaseInsensitive(string authHeaderLine)
        {
            var input = $"Method: GET, RequestUri: 'https://management.azure.com/subscriptions', Version: 1.1, Content: <null>, Headers:\r\n{{\r\n  {authHeaderLine}\r\n}}";

            var result = LoggingHandler.GetRedactedRequestString(input);

            result.Should().Contain("[REDACTED]");
            result.Should().NotContain("my-secret-token");
        }
    }
}
