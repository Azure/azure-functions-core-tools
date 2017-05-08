using Newtonsoft.Json;

namespace Azure.Functions.Cli.Common
{
    internal class HostStartSettings
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LocalHttpPort { get; set; }

        [JsonProperty("CORS", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Cors { get; set; }
    }
}
