using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Azure.Functions.Cli.Helpers
{
    public static class JavaHelper
    {
        public static string GetJavaVersion(string functionAppRoot)
        {
            string pomXmlPath = Path.Combine(functionAppRoot, "pom.xml");
            if (File.Exists(pomXmlPath))
            {
                var xmlDoc = XDocument.Load(pomXmlPath);
                var versionElement = xmlDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "java.version");
                return versionElement?.Value;
            }
            return null;
        }
    }
}
