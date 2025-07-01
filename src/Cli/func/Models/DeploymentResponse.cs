// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Models
{
    [JsonObject]
    public class DeploymentResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("status")]
        public DeployStatus? Status { get; set; }
    }
}
