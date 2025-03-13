using Newtonsoft.Json;

namespace Azure.Functions.Cli.Common
{
    public class HostStartSettings
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int LocalHttpPort { get; set; }

        [JsonProperty("CORS", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Cors { get; set; }

        [JsonProperty("CORSCredentials", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool CorsCredentials { get; set; }
    }
}
