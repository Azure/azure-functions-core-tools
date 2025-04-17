// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmArrayWrapper<T>
    {
        [JsonProperty(PropertyName = "value")]
        public IEnumerable<ArmWrapper<T>> Value { get; set; }

        [JsonProperty(PropertyName = "nextLink")]
        public string NextLink { get; set; }
    }
}
