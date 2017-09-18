using System.Collections.Generic;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IProxyManager
    {
        IEnumerable<string> Templates { get; }
        string GetProxies();
        string GetProxy(string name);
        void AddProxy(string Name, string templateName);
        void AddProxy(string name, ProxyDefinition proxyDefinition);
        void DeleteProxy(string name);
    }
}
