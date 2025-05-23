// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class DeploymentV1Apps : BaseKubernetesResource<DeploymentSpecV1Apps>
    {
    }

    public class DeploymentSpecV1Apps : IKubernetesSpec
    {
        [JsonProperty("replicas")]
        public int Replicas { get; set; }

        [JsonProperty("selector")]
        public SelectorV1 Selector { get; set; }

        [JsonProperty("template")]
        public PodTemplateV1 Template { get; set; }
    }
}
