using System.Threading.Tasks;
using FluentAssertions;
using Azure.Functions.Cli.Arm;
using Xunit;

namespace Azure.Functions.Cli.Tests.ArmTests
{
    public  class SubscriptionArmManagerTests
    {
        [Fact]
        public async Task GetSubscriptions()
        {
            var client = AzureClientFactory.GetAzureClient();
            var authHelper = AzureClientFactory.GetAuthHelper();

            var armManager = new ArmManager(authHelper, client);
            var subscriptions = await armManager.GetSubscriptionsAsync();

            subscriptions.Should()
                .HaveCount(2);
        }
    }
}
