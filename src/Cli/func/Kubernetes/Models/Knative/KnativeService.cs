using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Cli.Kubernetes.Models.Knative
{

    public class Env
    {
        public string name { get; set; }
        public string value { get; set; }
    }

    public class KnativeContainer
    {
        public string image { get; set; }
        public List<Env> env { get; set; }
    }

    public class RevisionTemplateSpec
    {
        public KnativeContainer container { get; set; }
    }

    public class RevisionTemplateMetadata
    {
        public Dictionary<string,string> annotations { get; set; }
    }

    public class RevisionTemplate
    {
        public RevisionTemplateSpec spec { get; set; }
        public RevisionTemplateMetadata metadata { get; set; }

    }

    public class Configuration
    {
        public RevisionTemplate revisionTemplate { get; set; }
    }

    public class RunLatest
    {
        public Configuration configuration { get; set; }
    }

    public class KnativeSpec
    {
        public RunLatest runLatest { get; set; }
    }

    public class KnativeService
    {
        public string apiVersion { get; set; }
        public string kind { get; set; }
        public KnativeSpec spec { get; set; }

        public Metadata metadata { get; set; }
    }

    public class Metadata
    {
        public string name { get; set; }
        public string @namespace { get; set; }
    }
}