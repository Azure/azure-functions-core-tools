// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Knative
{
    public class KnativeService
    {
        [JsonProperty("apiVersion")]
        public string ApiVersion { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("spec")]
        public KnativeSpec Spec { get; set; }

        [JsonProperty("metadata")]
        public Metadata Metadata { get; set; }
    }

    public class Env
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }

    public class KnativeContainer
    {
        [JsonProperty("image")]
        public string Image { get; set; }

        [JsonProperty("env")]
        public List<Env> Env { get; set; }
    }

    public class RevisionTemplateSpec
    {
        [JsonProperty("container")]
        public KnativeContainer Container { get; set; }
    }

    public class RevisionTemplateMetadata
    {
        [JsonProperty("annotations")]
        public Dictionary<string, string> Annotations { get; set; }
    }

    public class RevisionTemplate
    {
        [JsonProperty("spec")]
        public RevisionTemplateSpec Spec { get; set; }

        [JsonProperty("metadata")]
        public RevisionTemplateMetadata Metadata { get; set; }
    }

    public class Configuration
    {
        [JsonProperty("revisionTemplate")]
        public RevisionTemplate RevisionTemplate { get; set; }
    }

    public class RunLatest
    {
        [JsonProperty("configuration")]
        public Configuration Configuration { get; set; }
    }

    public class KnativeSpec
    {
        [JsonProperty("runLatest")]
        public RunLatest RunLatest { get; set; }
    }

    public class Metadata
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("@namespace")]
        public string @Namespace { get; set; }
    }
}
