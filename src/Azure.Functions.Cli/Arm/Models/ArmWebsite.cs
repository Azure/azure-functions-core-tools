using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsite
    {
        [JsonProperty(PropertyName = "enabledHostNames")]
        public IEnumerable<string> EnabledHostNames { get; set; }

        [JsonProperty(PropertyName = "sku")]
        public string Sku { get; set; }
    }
}