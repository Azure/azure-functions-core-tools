
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Models
{
    [JsonObject]
    public class DeploymentLogResponse
    {
        [JsonProperty("log_time")]
        public DateTime LogTime { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("details_url")]
        public string DetailsUrlString { get; set; }
    }
}