using Newtonsoft.Json;
using System.Collections.Generic;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class SelectorV1
    {
        [JsonProperty("matchLabels")]
        public IDictionary<string, string> MatchLabels { get; set; }
    }
}
