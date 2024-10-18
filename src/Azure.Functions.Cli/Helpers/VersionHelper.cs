using Azure.Functions.Cli.Common;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    internal class VersionHelper
    {
        public static async Task<string> RunAsync(Task<bool> isRunningOlderVersion = null)
        {
            isRunningOlderVersion ??= IsRunningAnOlderVersion();

            var isOlderVersion = await isRunningOlderVersion;

            var multipleInstallsWarning = await GetMultipleInstallationMessage(isOlderVersion);

            if (!string.IsNullOrEmpty(multipleInstallsWarning))
            {
                return multipleInstallsWarning;
            }

            return isOlderVersion ? Constants.OldCoreToolsVersionMessage : string.Empty;
        }

        // Check that current core tools is the latest version. 
        // To ensure that it doesn't block other tasks. The HTTP Request timeout is only 1 second. 
        // We simply ingnore the exception if for any reason the check fails. 
        public static async Task<bool> IsRunningAnOlderVersion()
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

                var latestCoreToolsReleaseVersion = releaseList.FirstOrDefault(x => x.Release == data.Tags.V4Release.ReleaseVersion)?.CoreToolsReleaseNumber;

                if (!string.IsNullOrEmpty(latestCoreToolsReleaseVersion) &&
                    Constants.CliVersion != latestCoreToolsReleaseVersion)
                {
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                // ignore exception and no warning when the check fails.
                return false;
            }
        }

        private static async Task<string> GetMultipleInstallationMessage(bool isRunningOldVersion)
        {
            try
            {
                var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new Executable("where.exe", "func.exe", workingDirectory: Environment.SystemDirectory)
                : new Executable("where", "func");

                var stdout = new List<string>();
                var exitCode = await command.RunAsync(o => stdout.Add(o));
                if (exitCode == 0)
                {
                    var funcPathList = stdout.Where(x => x != null).Select(x => x.Trim(' ', '\n', '\r', '"')).ToList();
                    if (funcPathList.Count > 1)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Multiple Azure Functions Core Tools installations found:");
                        foreach (var path in funcPathList)
                        {
                            sb.AppendLine(path);
                        }

                        sb.AppendLine();

                        var fileInfo = new FileInfo(Assembly.GetExecutingAssembly().Location);
                        if (isRunningOldVersion)
                        {
                            sb.AppendLine($"You are currently using an old Core Tools version of {Constants.CliVersion} which is installed at {fileInfo.Directory}. Please upgrade to the latest version.");
                        }
                        else
                        {
                            sb.AppendLine($"You are currently using Core Tools version {Constants.CliVersion} which is installed at {fileInfo.Directory}.");
                        }
                        
                        return sb.ToString();
                    }
                }
                else
                {
                    return string.Empty;
                }
            }
            catch (Exception)
            {
                // ignore the error. We don't want to throw exception becasue of version check. 
            }

            return string.Empty;
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

                    Uri uri = new UriBuilder(ReleaseDetail?.DownloadLink).Uri;

                    if (uri.Segments.Length < 4)
                    {
                        return string.Empty;
                    }

                    return uri.Segments[2].Replace("/", string.Empty);
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
