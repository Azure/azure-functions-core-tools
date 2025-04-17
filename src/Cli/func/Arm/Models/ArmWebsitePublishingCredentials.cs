// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsitePublishingCredentials
    {
        [JsonProperty(PropertyName = "publishingUserName")]
        public string PublishingUserName { get; set; }

        [JsonProperty(PropertyName = "publishingPassword")]
        public string PublishingPassword { get; set; }
    }
}
