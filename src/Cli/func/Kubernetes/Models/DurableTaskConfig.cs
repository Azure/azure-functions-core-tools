// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.Models
{
    internal class DurableTaskConfig
    {
        [JsonProperty("maxConcurrentOrchestratorFunctions")]
        public int MaxConcurrentOrchestratorFunctions { get; set; } = 10;

        [JsonProperty("maxConcurrentActivityFunctions")]
        public int MaxConcurrentActivityFunctions { get; set; } = 1;

        [JsonProperty("storageProvider")]
        public JObject StorageProvider { get; set; }
    }
}
