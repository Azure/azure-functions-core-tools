// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public abstract class BaseKubernetesResource<T> : IKubernetesResource where T : IKubernetesSpec
    {
        [JsonProperty("apiVersion")]
        public virtual string ApiVersion { get; set; }

        [JsonProperty("kind")]
        public virtual string Kind { get; set; }

        [JsonProperty("metadata")]
        public ObjectMetadataV1 Metadata { get; set; }

        [JsonProperty("spec")]
        public T Spec { get; set; }
    }
}
