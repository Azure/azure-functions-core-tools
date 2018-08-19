using System.Collections.Generic;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsite
    {
        public IEnumerable<string> enabledHostNames { get; set; }

        public string sku { get; set; }
    }
}