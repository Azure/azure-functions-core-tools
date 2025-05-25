// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class BasicAuthCheckResponse
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string ResourceName { get; set; }

        [JsonProperty(PropertyName = "type")]
        public string ResourceType { get; set; }

        [JsonProperty(PropertyName = "location")]
        public string Location { get; set; }

        [JsonProperty(PropertyName = "properties")]
        public BasicAuthCheckResponseProperties Properties { get; set; }
    }

    internal class BasicAuthCheckResponseProperties
    {
        [JsonProperty(PropertyName = "allow")]
        public bool Allow { get; set; }
    }
}
