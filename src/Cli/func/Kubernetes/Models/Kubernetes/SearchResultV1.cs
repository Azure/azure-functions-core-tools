using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class SearchResultV1<T>
    {
        [JsonProperty("apiVersion")]
        public string ApiVersion { get; set; }

        [JsonProperty("items")]
        public IEnumerable<T> Items { get; set; }
    }
}
