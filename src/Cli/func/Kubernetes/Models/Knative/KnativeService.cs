// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Kubernetes.Models.Knative
{
#pragma warning disable SA1649 // File name should match first type name
    public class Env
#pragma warning restore SA1649 // File name should match first type name
    {
        public string Name { get; set; }

        public string Value { get; set; }
    }

    public class KnativeContainer
    {
        public string Image { get; set; }

        public List<Env> Env { get; set; }
    }

    public class RevisionTemplateSpec
    {
        public KnativeContainer Container { get; set; }
    }

    public class RevisionTemplateMetadata
    {
        public Dictionary<string, string> Annotations { get; set; }
    }

    public class RevisionTemplate
    {
        public RevisionTemplateSpec Spec { get; set; }

        public RevisionTemplateMetadata Metadata { get; set; }
    }

    public class Configuration
    {
        public RevisionTemplate RevisionTemplate { get; set; }
    }

    public class RunLatest
    {
        public Configuration Configuration { get; set; }
    }

    public class KnativeSpec
    {
        public RunLatest RunLatest { get; set; }
    }

    public class KnativeService
    {
        public string ApiVersion { get; set; }

        public string Kind { get; set; }

        public KnativeSpec Spec { get; set; }

        public Metadata Metadata { get; set; }
    }

    public class Metadata
    {
        public string Name { get; set; }

        public string @Namespace { get; set; }
    }
}
