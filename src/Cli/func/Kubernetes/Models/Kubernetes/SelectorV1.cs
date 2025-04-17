// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class SelectorV1
    {
        [JsonProperty("matchLabels")]
        public IDictionary<string, string> MatchLabels { get; set; }
    }
}
