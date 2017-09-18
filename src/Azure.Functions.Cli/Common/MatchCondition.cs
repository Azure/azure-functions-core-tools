using System.Runtime.Serialization;

namespace Azure.Functions.Cli.Common
{
    [DataContract]
    public class MatchCondition
    {
        [DataMember(Name = "methods", EmitDefaultValue = false)]
        public string[] HttpMethods { get; set; }

        [DataMember(Name = "route", EmitDefaultValue = false)]
        public string Route { get; set; }
    }
}