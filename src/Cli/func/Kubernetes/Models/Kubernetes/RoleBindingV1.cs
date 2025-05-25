// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Kubernetes
{
    public class RoleBindingV1 : BaseKubernetesResource<IKubernetesSpec>
    {
        [JsonProperty("roleRef")]
        public RoleSubjectV1 RoleRef { get; set; }

        [JsonProperty("subjects")]
        public IEnumerable<RoleSubjectV1> Subjects { get; set; }
    }

    public class RoleSubjectV1
    {
        [JsonProperty("apiGroup")]
        public string ApiGroup { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
