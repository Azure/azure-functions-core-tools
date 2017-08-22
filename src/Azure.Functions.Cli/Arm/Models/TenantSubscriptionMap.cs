using System.Collections.Generic;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class TenantSubscriptionMap
    {
        public string TenantId { get; set; }
        public IEnumerable<ArmSubscription> Subscriptions { get; set; }
    }
}