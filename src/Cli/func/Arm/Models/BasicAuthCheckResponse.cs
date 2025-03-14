using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static NuGet.Client.ManagedCodeConventions;

namespace Azure.Functions.Cli.Arm.Models
{
    class BasicAuthCheckResponse
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string ResourceName { get; set; }

        [JsonProperty(PropertyName = "type")]
        public string ResourceType { get; set; }

        [JsonProperty(PropertyName = "location")]
        public string Location { get; set; }

        [JsonProperty(PropertyName = "properties")]
        public BasicAuthCheckResponseProperties Properties { get; set; }
    }

    class BasicAuthCheckResponseProperties
    {
        [JsonProperty(PropertyName = "allow")]
        public bool Allow { get; set; }
    }
}
