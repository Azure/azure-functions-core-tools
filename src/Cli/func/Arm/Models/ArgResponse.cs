using Newtonsoft.Json;
using System.Collections.Generic;

namespace Azure.Functions.Cli.Arm.Models
{
    class ArgResponse
    {
        [JsonProperty(PropertyName = "count")]
        public int Count { get; set; }

        [JsonProperty(PropertyName = "data")]
        public ArgData Data { get; set; }
    }

    class ArgData
    {
        [JsonProperty(PropertyName = "columns")]
        public IEnumerable<ArgColumn> Columns { get; set; }

        [JsonProperty(PropertyName = "rows")]
        public IEnumerable<IEnumerable<string>> Rows { get; set; }
    }

    class ArgColumn
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }
    }
}
