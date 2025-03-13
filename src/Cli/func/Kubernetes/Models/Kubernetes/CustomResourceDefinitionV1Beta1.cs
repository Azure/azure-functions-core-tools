using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class CustomResourceDefinitionV1Beta1 : BaseKubernetesResource<CustomResourceDefinitionSpecV1Beta1>
    { }

    public class CustomResourceDefinitionSpecV1Beta1 : IKubernetesSpec
    {
        [JsonProperty("group")]
        public string Group { get; internal set; }

        [JsonProperty("version")]
        public string Version { get; internal set; }

        [JsonProperty("names")]
        public CustomResourceDefinitionNamesV1Beta1 Names { get; internal set; }

        [JsonProperty("scope")]
        public string Scope { get; internal set; }
    }

    public class CustomResourceDefinitionNamesV1Beta1
    {
        [JsonProperty("kind")]
        public string Kind { get; internal set; }

        [JsonProperty("plural")]
        public string Plural { get; internal set; }
    }
}