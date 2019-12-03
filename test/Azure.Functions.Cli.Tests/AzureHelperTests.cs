using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Xunit;

namespace Azure.Functions.Cli.Helpers.Tests
{
    public class AzureHelperTests
    {
        const string managementUrl = "https://example.com";
        const string appId = "subscriptions/000/resourceGroups/000/Microsoft.Web/sites/000";
        const string accessToken = "accessToken";

        [Theory]
        [InlineData("default", "default-value")]
        [InlineData("another-key", "another-value")]
        public async Task GetFunctionKeyTest(string key, string value)
        {
            try
            {
                const string functionName = "function1";
                var mockUrl = $"{managementUrl}{appId}/functions/{functionName}/listKeys?api-version={ArmUriTemplates.WebsitesApiVersion}";

                var mockHttp = new MockHttpMessageHandler();
                mockHttp.When(HttpMethod.Post, mockUrl)
                        .Respond("application/json", $"{{'{key}': '{value}'}}");

                ArmClient.SetTestHandler(mockHttp);

                var result = await AzureHelper.GetFunctionKey(functionName, appId, accessToken, managementUrl);
                result.Should().Be(value);
            }
            finally
            {
                ArmClient.SetTestHandler(null);
            }
        }
    }
}