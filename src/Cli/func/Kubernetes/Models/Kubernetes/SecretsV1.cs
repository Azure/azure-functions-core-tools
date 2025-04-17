// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class SecretsV1 : BaseKubernetesResource<IKubernetesSpec>
    {
        [JsonProperty("data")]
        public IDictionary<string, string> Data { get; set; }

        [JsonProperty("type")]
        public string Type { get; internal set; }
    }
}
