using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class AppServiceConnectionString
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("type")]
        public int Type { get; set; }
    }
}