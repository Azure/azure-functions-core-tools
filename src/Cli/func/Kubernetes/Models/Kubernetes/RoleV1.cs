// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class RoleV1 : BaseKubernetesResource<IKubernetesSpec>
    {
        [JsonProperty("rules")]
        public IEnumerable<RuleV1> Rules { get; set; }
    }

    public class RuleV1
    {
        [JsonProperty("apiGroups")]
        public IEnumerable<string> ApiGroups { get; set; }

        [JsonProperty("resources")]
        public IEnumerable<string> Resources { get; set; }

        [JsonProperty("resourceNames")]
        public IEnumerable<string> ResourceNames { get; set; }

        [JsonProperty("verbs")]
        public IEnumerable<string> Verbs { get; set; }
    }
}
