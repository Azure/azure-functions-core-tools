// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Kubernetes.Models
{
    public class TriggersPayload
    {
        [JsonProperty("hostJson")]
        public JObject HostJson { get; set; }

        [JsonProperty("functionsJson")]
        public IDictionary<string, JObject> FunctionsJson { get; set; }
    }
}
