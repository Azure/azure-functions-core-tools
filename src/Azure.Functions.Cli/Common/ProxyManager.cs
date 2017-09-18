using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;
using Azure.Functions.Cli.Helpers;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace Azure.Functions.Cli.Common
{
    internal class ProxyManager : IProxyManager
    {
        private static string proxyResourcePrefix = "Azure.Functions.Cli.azurefunctions.proxy";

        public IEnumerable<string> Templates
        {
            get
            {
                return GetTemplates();
            }
        }

        public static string ProxyFilePath
        {
            get
            {
                var rootPath = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
                var filePath = Path.Combine(rootPath, Constants.ProxiesFileName);
                return filePath;
            }
        }

        public void DeleteProxy(string name)
        {
            var proxyFile = new ProxyFile(ProxyFilePath);
            proxyFile.DeleteProxy(name);
            proxyFile.Commit();
        }

        public string GetProxies()
        {
            var proxyFile = new ProxyFile(ProxyFilePath);
            return proxyFile.GetProxies();
        }

        public string GetProxy(string name)
        {
            var proxyFile = new ProxyFile(ProxyFilePath);
            return proxyFile.GetProxy(name);
        }

        public void AddProxy(string name, ProxyDefinition proxyDefinition)
        {
            var proxyFile = new ProxyFile(ProxyFilePath);
            proxyFile.AddProxy(name, proxyDefinition);
            proxyFile.Commit();
        }

        public void AddProxy(string name, string templateName)
        {
            string result;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(string.Format("{0}.{1}.json", proxyResourcePrefix, templateName)))
            using (StreamReader reader = new StreamReader(stream))
            {
                result = reader.ReadToEnd();
            }

            var proxyDefinition = JsonConvert.DeserializeObject<ProxyDefinition>(result);

            AddProxy(name, proxyDefinition);
        }

        private static IEnumerable<string> GetTemplates()
        {
            var templates = new List<string>();

            foreach (var str in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if(str.StartsWith(proxyResourcePrefix) && str.EndsWith(".json"))
                {
                    var resourceName = str.Remove(0, proxyResourcePrefix.Length + 1);
                    resourceName = resourceName.Remove(resourceName.LastIndexOf(".json"), 5);
                    templates.Add(resourceName);
                }
            }

            return templates;
        }

        private class ProxyFile
        {
            private readonly string _filePath;

            public ProxyFile(string filePath)
            {
                _filePath = filePath;
                try
                {
                    var content = FileSystemHelpers.ReadAllTextFromFile(_filePath);
                    var proxyConfig = JsonConvert.DeserializeObject<ProxyConfig>(content);
                    Proxies = proxyConfig.ProxyMap;
                }
                catch
                {
                    Proxies = new Dictionary<string, ProxyDefinition>();
                }
            }

            public Dictionary<string, ProxyDefinition> Proxies { get; set; } = new Dictionary<string, ProxyDefinition>();

            public void AddProxy(string name, ProxyDefinition proxyDefinition)
            {
                if (Proxies.ContainsKey(name))
                {
                    var response = "n";
                    do
                    {
                        ColoredConsole.Write($"A proxy with the name {name} already exists. Overwrite [y/n]? [n] ");
                        response = Console.ReadLine();
                    } while (response != "n" && response != "y");
                    if (response == "n")
                    {
                        return;
                    }
                    Proxies[name] = proxyDefinition;
                }
                else
                {
                    Proxies.Add(name, proxyDefinition);
                }
            }

            public void DeleteProxy(string name)
            {
                if (Proxies.ContainsKey(name))
                {
                    Proxies.Remove(name);
                }
            }

            public string GetProxy(string name)
            {
                if (Proxies.ContainsKey(name))
                {
                    return JsonConvert.SerializeObject(Proxies[name], Formatting.Indented);
                }
                else
                {
                    ColoredConsole.Write($"A proxy with the name {name} does not exist.");
                    return string.Empty;
                }
            }

            public string GetProxies()
            {
                    return JsonConvert.SerializeObject(Proxies, Formatting.Indented);
            }

            public void Commit()
            {
                FileSystemHelpers.WriteAllTextToFile(_filePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
        }
    }
}
