// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.KEDA.Models
{
    public class ScaledObjectTriggerV1Alpha1
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("metadata")]
        public IDictionary<string, string> Metadata { get; set; }
    }
}
