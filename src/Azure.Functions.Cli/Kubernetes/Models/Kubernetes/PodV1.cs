
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class PodTemplateV1 : BaseKubernetesResource<PodTemplateSpecV1>
    {

    }

    public class PodTemplateSpecV1 : IKubernetesSpec
    {
        [JsonProperty("containers")]
        public IEnumerable<ContainerV1> Containers { get; set; }

        [JsonProperty("insPolicy")]
        public string DnsPolicy { get; set; }

        [JsonProperty("imagePullSecrets")]
        public IEnumerable<ImagePullSecretV1> ImagePullSecrets { get; set; }

        [JsonProperty("toleration")]
        public IEnumerable<PodTolerationV1> Tolerations { get; set; }

        [JsonProperty("serviceAccountName")]
        public string ServiceAccountName { get; internal set; }
    }

    public class PodTolerationV1
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("effect")]
        public string Effect { get; set; }
    }

    public class ImagePullSecretV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }
}