using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmSubscription
    {
        [JsonProperty(PropertyName = "subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonProperty(PropertyName = "displayName")]
        public string DisplayName { get; set; }
    }
}