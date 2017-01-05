using System.Threading.Tasks;
using FluentAssertions;
using Azure.Functions.Cli.Arm;
using Xunit;
using NSubstitute;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Tests.ArmTests
{
    public  class SubscriptionArmManagerTests
    {
        [Fact]
        public async Task GetSubscriptions()
        {
            var client = AzureClientFactory.GetAzureClient();
            var authHelper = AzureClientFactory.GetAuthHelper();
            var settings = Substitute.For<ISettings>();

            var armManager = new ArmManager(authHelper, client, settings);
            var subscriptions = await armManager.GetSubscriptionsAsync();

            subscriptions.Should()
                .HaveCount(2);
        }
    }
}
