using System;
using System.IO;
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

        // Default timeout in seconds to retrieve the module version.
        private const int DefaultTimeOutInSeconds = 10;

        /// <summary>
        /// Gets the latest supported major version of the Az module from the PSGallery.
        /// </summary>
        public static async Task<string> GetLatestAzModuleMajorVersion()
        {
            Uri address = new Uri($"{PowerShellGalleryFindPackagesByIdUri}'{AzModuleName}'");

            string latestMajorVersion = null;
            Stream stream = null;

            await RetryHelper.Retry(async () =>
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", Constants.CliUserAgent);
                    client.Timeout = TimeSpan.FromSeconds(DefaultTimeOutInSeconds);

                    try
                    {
                        var response = await client.GetAsync(address);
                        // Throw is not a successful request
                        response.EnsureSuccessStatusCode();
                        stream = await response.Content.ReadAsStreamAsync();
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $@"Fail to get module version for {AzModuleName}. Errors: {ex.Message}";
                        throw new CliException(errorMsg);
                    }
                }
            }, retryCount: 3);

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
                var props = root.SelectNodes("//m:properties[d:IsPrerelease = \"false\"]/d:Version", nsmgr);
                var latestVersion = new Version("0.0");

                if (props != null && props.Count > 0)
                {
                    foreach (XmlNode prop in props)
                    {
                        Version.TryParse(prop.FirstChild.Value, out var currentVersion);

                        if (currentVersion != null && currentVersion > latestVersion)
                        {
                            latestVersion = currentVersion;
                        }
                    }
                }

                latestMajorVersion = latestVersion.ToString().Split(".")[0];
            }

            if (string.IsNullOrEmpty(latestMajorVersion))
            {
                throw new CliException($@"Fail to get module version for {AzModuleName}.");
            }

            return latestMajorVersion;
        }
    }
}
