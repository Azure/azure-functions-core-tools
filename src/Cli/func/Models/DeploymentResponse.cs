
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;
using Azure.Functions.Cli.Common;

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