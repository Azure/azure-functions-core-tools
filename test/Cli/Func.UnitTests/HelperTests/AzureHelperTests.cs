// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
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

        [Fact]
        public async Task GetStorageAccount_ShouldReturn_WhenStorageExists()
        {
            try
            {
                const string managementUrl = "https://management.azure.com";
                const string subscriptionId = "sub";
                const string storageName = "mystorage";
                const string accessToken = "token";

                var subscriptionsUrl =
                    $"{managementUrl}/subscriptions?api-version=2018-09-01";

                var argUrl =
                    $"{managementUrl}/providers/Microsoft.ResourceGraph/resources?api-version=2019-04-01";

                var keysUrl =
                    $"{managementUrl}/subscriptions/{subscriptionId}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/{storageName}/listKeys?api-version=2018-02-01";

                var resourceId =
                    $"/subscriptions/{subscriptionId}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/{storageName}";

                var mockHttp = new MockHttpMessageHandler();

                // Mock subscriptions
                mockHttp.When(HttpMethod.Get, subscriptionsUrl).Respond("application/json", @"{'value': [{ 'subscriptionId': 'sub', 'displayName': 'Test Sub' }]}");

                // Mock ARG response
                mockHttp.When(HttpMethod.Post, argUrl).Respond("application/json", $@"{{'data': {{'rows': [[ '{resourceId}' ]]}}}}");

                // Mock listKeys
                mockHttp.When(HttpMethod.Post, keysUrl).Respond("application/json", @"{'keys': [{ 'keyName': 'key1', 'value': 'test-key', 'permissions': 'FULL' }]}");

                ArmClient.SetTestHandler(mockHttp);

                // Act
                var result = await AzureHelper.GetStorageAccount(
                    storageName,
                    accessToken,
                    managementUrl);

                // Assert
                result.Should().NotBeNull();
                result.StorageAccountName.Should().Be(storageName);
                result.StorageAccountKey.Should().Be("test-key");
            }
            finally
            {
                ArmClient.SetTestHandler(null);
            }
        }

        [Fact]
        public async Task GetStorageAccount_ShouldThrow_WhenStorageNotFound()
        {
            try
            {
                const string managementUrl = "https://management.azure.com";
                const string accessToken = "token";
                const string storageName = "mystorage";

                var subscriptionsUrl =
                    $"{managementUrl}/subscriptions?api-version=2018-09-01";

                var argUrl =
                    $"{managementUrl}/providers/Microsoft.ResourceGraph/resources?api-version=2019-04-01";

                var mockHttp = new MockHttpMessageHandler();

                // Mock subscriptions
                mockHttp.When(HttpMethod.Get, subscriptionsUrl).Respond("application/json", @"{'value': [{ 'subscriptionId': 'sub', 'displayName': 'Test Sub' }]}");

                // ARG returns empty
                mockHttp.When(HttpMethod.Post, argUrl).Respond("application/json", @"{'data':{rows: []}}");

                ArmClient.SetTestHandler(mockHttp);

                // Act + Assert
                await Assert.ThrowsAsync<CliException>(() =>
                    AzureHelper.GetStorageAccount(storageName, accessToken, managementUrl));
            }
            finally
            {
                ArmClient.SetTestHandler(null);
            }
        }
    }
}
