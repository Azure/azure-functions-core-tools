using System.Collections.Generic;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmSubscriptionsArray
    {
        public IEnumerable<ArmSubscription> value { get; set; }
        public string nextLink { get; set; }
    }
}