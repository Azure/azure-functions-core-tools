using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmSubscriptionsArray
    {
        [JsonProperty(PropertyName = "value")]
        public IEnumerable<ArmSubscription> Value { get; set; }
    }
}