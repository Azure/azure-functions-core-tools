using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Azure.Functions.Cli.Common
{
    [DataContract]
    public class ProxyConfig
    {
        [DataMember(Name = "proxies")]
        public Dictionary<string, ProxyDefinition> ProxyMap { get; set; }
    }
}