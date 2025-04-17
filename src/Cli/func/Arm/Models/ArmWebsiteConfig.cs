// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsiteConfig
    {
        [JsonProperty(PropertyName = "scmType")]
        public string ScmType { get; set; }

        [JsonProperty(PropertyName = "linuxFxVersion")]
        public string LinuxFxVersion { get; set; }

        [JsonProperty(PropertyName = "netFrameworkVersion")]
        public string NetFrameworkVersion { get; set; }
    }
}
