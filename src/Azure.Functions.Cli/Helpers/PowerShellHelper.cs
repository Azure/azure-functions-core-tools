using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using System.Net.Http;
using System.Xml;

namespace Azure.Functions.Cli.Helpers
{
    public static class PowerShellHelper
    {
        // The module name for the Azure Resource Manager.
        private const string AzModuleName = "Az";

        // The PowerShellGallery uri to query for the latest module version.
        private const string PowerShellGalleryFindPackagesByIdUri = "https://www.powershellgallery.com/api/v2/FindPackagesById()?id=";
        
        /// <summary>
        /// Gets the latest supported major version of the Az module from the PSGallery.
        /// </summary>
        public static async Task<string> GetLatestAzModuleMajorVersion()
        {
            Uri address = new Uri($"{PowerShellGalleryFindPackagesByIdUri}'{AzModuleName}'");
            int numberOfRetries = 3;

            string latestMajorVersion = null;
            string errorMessage = $@"Fail to get module version for {AzModuleName}";

            HttpResponseMessage response = null;
            using (HttpClient client = new HttpClient())
            {
                bool successfulRequest = false;
                do
                {
                    response = await client.GetAsync(address);
                    successfulRequest = response.IsSuccessStatusCode;
                    numberOfRetries--;
                } while (!successfulRequest && numberOfRetries <= 0);
            }

            if (response == null)
            {
                throw new CliException(errorMessage);
            }

            var stream = response.Content.ReadAsStreamAsync().Result;
            if (stream != null)
            {
                // Load up the XML response
                XmlDocument doc = new XmlDocument();
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    doc.Load(reader);
                }

                // Add the namespaces for the gallery xml content
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("ps", "http://www.w3.org/2005/Atom");
                nsmgr.AddNamespace("d", "http://schemas.microsoft.com/ado/2007/08/dataservices");
                nsmgr.AddNamespace("m", "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata");

                // Find the version information
                XmlNode root = doc.DocumentElement;
                var props = root.SelectNodes("//m:properties/d:Version", nsmgr);
                var latestVersion = new Version("0.0");

                if (props != null && props.Count > 0)
                {
                    for (int i = 0; i < props.Count; i++)
                    {
                        var currentVersion = new Version(props[i].FirstChild.Value);

                        var result = currentVersion.CompareTo(latestVersion);
                        if (result > 0)
                        {
                            latestVersion = currentVersion;
                        }
                    }
                }

                latestMajorVersion = latestVersion.ToString().Split(".")[0];
            }

            if (string.IsNullOrEmpty(latestMajorVersion))
            {
                throw new CliException(errorMessage);
            }

            return latestMajorVersion;
        }
    }
}
