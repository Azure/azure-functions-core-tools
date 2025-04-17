// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArgResponse
    {
        [JsonProperty(PropertyName = "count")]
        public int Count { get; set; }

        [JsonProperty(PropertyName = "data")]
        public ArgData Data { get; set; }
    }

    internal class ArgData
    {
        [JsonProperty(PropertyName = "columns")]
        public IEnumerable<ArgColumn> Columns { get; set; }

        [JsonProperty(PropertyName = "rows")]
        public IEnumerable<IEnumerable<string>> Rows { get; set; }
    }

    internal class ArgColumn
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }
    }
}
