// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
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

        [Fact]
        public async Task GetStorageAccount_ShouldReturn_WhenStorageExists()
        {
            try
            {
                const string managementUrl = "https://management.azure.com";
                const string subscriptionId = "sub";
                const string storageName = "mystorage";
                const string resourceGroup = "rg";
                const string accessToken = "token";

                var listUrl =
                    $"{managementUrl}/subscriptions/{subscriptionId}/providers/Microsoft.Storage/storageAccounts?api-version=2018-02-01";

                var getUrl =
                    $"{managementUrl}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Storage/storageAccounts/{storageName}?api-version=2018-02-01";

                var keysUrl =
                    $"{managementUrl}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Storage/storageAccounts/{storageName}/listKeys?api-version=2018-02-01";

                var subscriptionsUrl =
                    $"{managementUrl}/subscriptions?api-version=2018-09-01";

                var mockHttp = new MockHttpMessageHandler();

                // Mock subscriptions
                mockHttp.When(HttpMethod.Get, subscriptionsUrl).Respond("application/json", @"{'value': [{ 'subscriptionId': 'sub', 'displayName': 'Test Sub' }]}");

                // Mock list storage accounts
                mockHttp.When(HttpMethod.Get, listUrl).Respond("application/json", @"{'value': [{'name': 'mystorage','id': '/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/mystorage'}]}");

                // Mock get storage account
                mockHttp.When(HttpMethod.Get, getUrl).Respond("application/json", @"{'name': 'mystorage','id': '/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/mystorage'}");

                // Mock listKeys
                mockHttp.When(HttpMethod.Post, keysUrl).Respond("application/json", @"{'keys': [{'keyName': 'key1','value': 'test-key','permissions': 'FULL'}]}");

                // Attach mock handler
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
                const string subscriptionId = "sub";
                const string storageName = "mystorage";
                const string accessToken = "token";

                var subscriptionsUrl =
                    $"{managementUrl}/subscriptions?api-version=2018-09-01";

                var listUrl =
                    $"{managementUrl}/subscriptions/{subscriptionId}/providers/Microsoft.Storage/storageAccounts?api-version=2018-02-01";

                var mockHttp = new MockHttpMessageHandler();

                // Mock subscriptions
                mockHttp.When(HttpMethod.Get, subscriptionsUrl).Respond("application/json", @"{'value': [{ 'subscriptionId': 'sub', 'displayName': 'Test Sub' }]}");

                // Mock empty storage accounts list
                mockHttp.When(HttpMethod.Get, listUrl).Respond("application/json", @"{ 'value': [] }");

                ArmClient.SetTestHandler(mockHttp);

                // Act & Assert
                await Assert.ThrowsAsync<ArmResourceNotFoundException>(() => AzureHelper.GetStorageAccount(storageName, accessToken, managementUrl));
            }
            finally
            {
                ArmClient.SetTestHandler(null);
            }
        }

        [Fact]
        public async Task GetStorageAccount_ShouldThrow_LastException_WhenAllSubscriptionsFail()
        {
            try
            {
                const string managementUrl = "https://management.azure.com";
                const string accessToken = "token";

                var subscriptionsUrl =
                    $"{managementUrl}/subscriptions?api-version=2018-09-01";

                var listUrl =
                    $"{managementUrl}/subscriptions/sub/providers/Microsoft.Storage/storageAccounts?api-version=2018-02-01";

                var mockHttp = new MockHttpMessageHandler();

                // Subscriptions
                mockHttp.When(HttpMethod.Get, subscriptionsUrl).Respond("application/json", @"{ 'value': [ { 'subscriptionId': 'sub' } ] }");

                // Simulate failure (403)
                mockHttp.When(HttpMethod.Get, listUrl)
                    .Respond(HttpStatusCode.Forbidden);
                ArmClient.SetTestHandler(mockHttp);

                // Act & Assert
                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    AzureHelper.GetStorageAccount("mystorage", accessToken, managementUrl));
            }
            finally
            {
                ArmClient.SetTestHandler(null);
            }
        }
    }
}
