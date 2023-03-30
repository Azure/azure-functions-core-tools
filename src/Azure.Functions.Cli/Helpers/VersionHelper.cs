using Azure.Functions.Cli.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    internal class VersionHelper
    {

        // Check that current core tools is the latest version. 
        // To ensure that it doesn't block other tasks. The HTTP Request timeout is only 1 second. 
        // We simply ingnore the exception if for any reason the check fails. 
        public static async Task<string> CheckLatestVersionAsync()
        {
            try
            {
                var client = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(1)
                };
                var response = await client.GetAsync(Constants.CoreToolsVersionsFeedUrl);
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<CliFeed>(content);
                IEnumerable releases = ((IEnumerable) data.Releases);
                var releaseList = new List<ReleaseSummary>();
                foreach (var item in releases)
                {
                    var jProperty = (Newtonsoft.Json.Linq.JProperty)item;
                    var releaseDetail = JsonConvert.DeserializeObject<ReleaseDetail>(jProperty.Value.ToString());
                    releaseList.Add(new ReleaseSummary() { Release = jProperty.Name, ReleaseDetail = releaseDetail.ReleaseList.FirstOrDefault() });
                }

                var currentCoreToolsRelease = releaseList.FirstOrDefault(x => x.CoreToolsReleaseNumber == Constants.CliDetailedVersion[..Constants.CliDetailedVersion.IndexOf(" ")]);
                var latestCoreToolsRelease = releaseList.FirstOrDefault(x => x.Release == data.Tags.V4Release.ReleaseVersion);

                if ( !string.IsNullOrEmpty(currentCoreToolsRelease?.CoreToolsReleaseNumber) && 
                    !string.IsNullOrEmpty(latestCoreToolsRelease?.CoreToolsReleaseNumber) && 
                    currentCoreToolsRelease.CoreToolsReleaseNumber != latestCoreToolsRelease.CoreToolsReleaseNumber)
                {
                    return Constants.OldCoreToolsVersionMessage;
                }

                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private class CliFeed
        {
            [JsonProperty("tags")]
            public Tags Tags { get; set; }

            [JsonProperty("releases")]
            public Object Releases { get; set; }
        }

        private class Tags
        {
            [JsonProperty("v4")]
            public Release V4Release { get; set; }

            [JsonProperty("v4-prerelease")]
            public Release V4PreRelease { get; set; }

            [JsonProperty("v3")]
            public Release V3Release { get; set; }

            [JsonProperty("v3-prerelease")]
            public Release V3PreRelease { get; set; }
        }

        private class Release
        {
            [JsonProperty("release")]
            public string ReleaseVersion { get; set; }

            [JsonProperty("releaseQuality")]
            public string ReleaseQuality { get; set; }

            [JsonProperty("hidden")]
            public bool Hidden { get; set; }
        }

        private class ReleaseSummary
        {
            public string Release { get; set; }

            public string CoreToolsReleaseNumber
            {
                get
                {
                    var downloadLink = ReleaseDetail?.DownloadLink;
                    if (string.IsNullOrEmpty(ReleaseDetail?.DownloadLink))
                    {
                        return string.Empty;
                    }

                    downloadLink = downloadLink.Replace("https://functionscdn.azureedge.net/public/", string.Empty);
                    return downloadLink[..downloadLink.IndexOf('/')];
                }
            }
            public CoreToolsRelease ReleaseDetail { get; set; }
        }

        private class ReleaseDetail
        {
            [JsonProperty("coreTools")]
            public IList<CoreToolsRelease> ReleaseList { get; set; }
        }

        private class CoreToolsRelease
        {
            [JsonProperty("OS")]
            public string Os { get; set; }

            [JsonProperty("Architecture")]
            public string Architecture { get; set; }

            [JsonProperty("downloadLink")]
            public string DownloadLink { get; set; }

            [JsonProperty("size")]
            public string Size { get; set; }

            [JsonProperty("default")]
            public bool Default { get; set; }
        }
    }

    
}
