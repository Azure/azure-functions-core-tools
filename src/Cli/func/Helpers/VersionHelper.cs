// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Azure.Functions.Cli.Helpers
{
    internal class VersionHelper
    {
        // Set the CliVersion for testing
        internal static string CliVersion { get; set; } = Constants.CliVersion;

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
        public static async Task<bool> IsRunningAnOlderVersion(HttpClient client = null)
        {
            try
            {
                using (client ??= new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "AzureFunctionsCoreToolsClient");

                    var response = await client.GetAsync(Constants.GitHubReleaseApiUrl);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var release = JsonConvert.DeserializeObject<GitHubRelease>(content);

                    return !release.TagName.EqualsIgnoreCase(CliVersion);
                }
            }
            catch (Exception)
            {
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

        private class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }
        }
    }
}
