using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class ClusterRoleBindingV1 : BaseKubernetesResource<IKubernetesSpec>
    {
        [JsonProperty("roleRef")]
        public ClusterRoleBindingRoleRefV1 RoleRef { get; internal set; }

        [JsonProperty("subjects")]
        public ClusterRoleBindingSubjectsV1[] Subjects { get; internal set; }
    }

    public class ClusterRoleBindingSubjectsV1
    {
        [JsonProperty("kind")]
        public string Kind { get; internal set; }

        [JsonProperty("name")]
        public string Name { get; internal set; }

        [JsonProperty("namespace")]
        public string Namespace { get; internal set; }
    }

    public class ClusterRoleBindingRoleRefV1
    {
        [JsonProperty("kind")]
        public string Kind { get; internal set; }

        [JsonProperty("name")]
        public string Name { get; internal set; }

        [JsonProperty("apiGroup")]
        public string ApiGroup { get; internal set; }
    }
}