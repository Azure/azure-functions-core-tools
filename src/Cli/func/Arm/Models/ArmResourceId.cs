using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Arm.Models
{
    class ArmResourceId
    {
        public string Subscription { get; set; }

        public string ResourceGroup { get; set; }

        public string Provider { get; set; }

        public string Name { get; set; }

        public override string ToString()
        {
            return $@"/subscriptions/{Subscription}/resourceGroups/{ResourceGroup}/providers/{Provider}/components/{Name}";
        }
    }
}
