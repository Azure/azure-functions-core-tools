using Newtonsoft.Json;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    class TestArgResponse
    {
        [JsonProperty(PropertyName = "count")]
        public int Count { get; set; }

        [JsonProperty(PropertyName = "data")]
        public object Data { get; set; }
    }
}
