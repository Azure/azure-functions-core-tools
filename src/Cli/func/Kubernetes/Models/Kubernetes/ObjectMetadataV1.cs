// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ObjectMetadataV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("namespace")]
        public string Namespace { get; set; }

        [JsonProperty("labels")]
        public IDictionary<string, string> Labels { get; set; }

        [JsonProperty("annotations")]
        public IDictionary<string, string> Annotations { get; set; }
    }
}
