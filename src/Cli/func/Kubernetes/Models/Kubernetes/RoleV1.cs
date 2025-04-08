using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

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
