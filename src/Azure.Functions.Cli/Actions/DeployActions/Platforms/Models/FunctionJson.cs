using System.Collections.Generic;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms.Models
{
    internal class Binding
    {
        public string authLevel { get; set; }
        public string type { get; set; }
        public string direction { get; set; }
        public string name { get; set; }
        public List<string> methods { get; set; }
    }

    internal class FunctionJson
    {
        public bool disabled { get; set; }
        public List<Binding> bindings { get; set; }
        public string scriptFile { get; set; }
    }
}