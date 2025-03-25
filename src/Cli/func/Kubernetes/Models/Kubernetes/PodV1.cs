
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

        [JsonProperty("volumes")]
        public IEnumerable<VolumeV1> Volumes { get; set; }
    }
    public class VolumeV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("configMap")]
        public VolumeConfigMapV1 VolumeConfigMap { get; set; }

        [JsonProperty("secret")]
        public VolumeSecretV1 VolumeSecret { get; set; }
    }

    public class VolumeConfigMapV1
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class VolumeSecretV1
    {
        [JsonProperty("secretName")]
        public string SecretName { get; set; }
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