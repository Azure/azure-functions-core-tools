// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class AzureHelperTests
    {
        private const string ManagementUrl = "https://example.com";
        private const string AppId = "subscriptions/000/resourceGroups/000/Microsoft.Web/sites/000";
        private const string AccessToken = "AccessToken";

        [Theory]
        [InlineData("default", "default-value")]
        [InlineData("another-key", "another-value")]
        public async Task GetFunctionKeyTest(string key, string value)
        {
            try
            {
                const string functionName = "function1";
                var mockUrl = $"{ManagementUrl}{AppId}/functions/{functionName}/listKeys?api-version={ArmUriTemplates.WebsitesApiVersion}";

                var mockHttp = new MockHttpMessageHandler();
                mockHttp.When(HttpMethod.Post, mockUrl)
                        .Respond("application/json", $"{{'{key}': '{value}'}}");

                ArmClient.SetTestHandler(mockHttp);

                var result = await AzureHelper.GetFunctionKey(functionName, AppId, AccessToken, ManagementUrl);
                result.Should().Be(value);
            }
            finally
            {
                ArmClient.SetTestHandler(null);
            }
        }
    }
}
