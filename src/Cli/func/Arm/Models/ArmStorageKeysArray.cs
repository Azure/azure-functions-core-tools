// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    public class ArmStorageKeysArray
    {
        [JsonProperty(PropertyName = "keys")]
        public ArmStorageKeys[] Keys { get; set; }
    }
}
