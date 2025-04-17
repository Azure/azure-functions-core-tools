// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmResourceId
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
