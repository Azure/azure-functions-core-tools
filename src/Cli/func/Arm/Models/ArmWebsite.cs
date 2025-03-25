using System.Collections.Generic;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsite
    {
        public IEnumerable<string> enabledHostNames { get; set; }

        public string sku { get; set; }

        public FunctionAppConfig functionAppConfig { get; set; }
    }

    public class Authentication
    {
        public string type { get; set; }
        public object userAssignedIdentityResourceId { get; set; }
        public string storageAccountConnectionStringName { get; set; }
    }

    public class Deployment
    {
        public Storage storage { get; set; }
    }

    public class FunctionAppConfig
    {
        public Deployment deployment { get; set; }
        public Runtime runtime { get; set; }
        public ScaleAndConcurrency scaleAndConcurrency { get; set; }
    }

    public class Runtime
    {
        public string name { get; set; }
        public string version { get; set; }
    }

    public class ScaleAndConcurrency
    {
        public List<object> alwaysReady { get; set; }
        public int maximumInstanceCount { get; set; }
        public int instanceMemoryMB { get; set; }
        public object triggers { get; set; }
    }

    public class Storage
    {
        public string type { get; set; }
        public string value { get; set; }
        public Authentication authentication { get; set; }
    }
}