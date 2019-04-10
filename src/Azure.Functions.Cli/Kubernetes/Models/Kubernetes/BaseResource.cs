using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public interface IKubernetesResource
    { }

    public abstract class BaseKubernetesResource<T> : IKubernetesResource where T : IKubernetesSpec
    {
        [JsonProperty("apiVersion")]
        public string ApiVersion { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("metadata")]
        public ObjectMetadataV1 Metadata { get; set; }

        [JsonProperty("spec")]
        public T Spec { get; set; }
    }

    public class SelectorV1
    {
        [JsonProperty("matchLabels")]
        public IDictionary<string, string> MatchLabels { get; set; }
    }

    public class SearchResultV1<T>
    {
        [JsonProperty("apiVersion")]
        public string ApiVersion { get; set; }

        [JsonProperty("items")]
        public IEnumerable<T> Items { get; set; }
    }
}