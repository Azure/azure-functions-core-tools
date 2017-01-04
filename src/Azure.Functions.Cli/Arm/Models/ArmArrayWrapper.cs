using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmArrayWrapper<T>
    {
        [JsonProperty(PropertyName = "value")]
        public IEnumerable<ArmWrapper<T>> Value { get; set; }
    }
}