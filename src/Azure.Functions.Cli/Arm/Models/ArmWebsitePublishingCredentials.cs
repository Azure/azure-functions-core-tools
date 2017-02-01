using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsitePublishingCredentials
    {
        [JsonProperty("publishingUserName")]
        public string PublishingUserName { get; set; }

        [JsonProperty("publishingPassword")]
        public string PublishingPassword { get; set; }
    }
}