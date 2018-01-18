using System;
using System.Linq;
using System.Net;
using System.Xml.Linq;

namespace packageConfigParser
{
    class Program
    {
        static void Main(string[] args)
        {
            ParsePackageConfig("https://raw.githubusercontent.com/Azure/azure-webjobs-sdk-script/v1.x/src/WebJobs.Script.WebHost/packages.config");
            ParsePackageConfig("https://raw.githubusercontent.com/Azure/azure-webjobs-sdk-script/v1.x/src/WebJobs.Script/packages.config");
        }

        static void ParsePackageConfig(string url)
        {
            using (var client = new WebClient())
            {
                var content = client.DownloadString(new Uri(url));
                var doc = XDocument.Parse(content);
                var packages = doc
                    .Element("packages")
                    .Descendants()
                    .Select(e => new
                    {
                        Name = e.Attribute("id").Value,
                        Version = e.Attribute("version").Value
                    })
                    .Where(i => i.Name != "StyleCop.Analyzers");

                foreach (var p in packages)
                {
                    Console.WriteLine($"Install-Package {p.Name} -Version {p.Version}");
                }
            }
        }
    }
}
