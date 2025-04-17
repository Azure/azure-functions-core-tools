// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmArrayWrapper<T>
    {
        public IEnumerable<ArmWrapper<T>> Value { get; set; }

        public string NextLink { get; set; }
    }
}
