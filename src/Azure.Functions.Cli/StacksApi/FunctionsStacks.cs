using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.StacksApi
{
    public class FunctionsStacks
    {
        [JsonProperty("value")]
        public List<Language> Languages { get; set; }
    }

    public class Language
    {
        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public Properties Properties { get; set; }
    }

    public class Properties
    {
        [JsonProperty("displayText")]
        public string DisplayText { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("preferredOs")]
        public string PreferredOs { get; set; }

        [JsonProperty("majorVersions")]
        public List<MajorVersion> MajorVersions { get; set; }
    }

    public class MajorVersion
    {
        [JsonProperty("displayText")]
        public string DisplayText { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("minorVersions")]
        public List<MinorVersion> MinorVersions { get; set; }
    }

    public class MinorVersion
    {
        [JsonProperty("displayText")]
        public string DisplayText { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("stackSettings")]
        public StackSettings StackSettings { get; set; }
    }

    public class StackSettings
    {
        [JsonProperty("windowsRuntimeSettings")]
        public WindowsRuntimeSettings WindowsRuntimeSettings { get; set; }

        [JsonProperty("linuxRuntimeSettings")]
        public LinuxRuntimeSettings LinuxRuntimeSettings { get; set; }
    }

    public class WindowsRuntimeSettings
    {
        [JsonProperty("runtimeVersion")]
        public string RuntimeVersion { get; set; }

        [JsonProperty("remoteDebuggingSupported")]
        public bool RemoteDebuggingSupported { get; set; }

        [JsonProperty("appInsightsSettings")]
        public AppInsightsSettings AppInsightsSettings { get; set; }

        [JsonProperty("gitHubActionSettings")]
        public GitHubActionSettings GitHubActionSettings { get; set; }

        [JsonProperty("appSettingsDictionary")]
        public AppSettingsDictionary AppSettingsDictionary { get; set; }

        [JsonProperty("siteConfigPropertiesDictionary")]
        public SiteConfigPropertiesDictionary SiteConfigPropertiesDictionary { get; set; }

        [JsonProperty("supportedFunctionsExtensionVersions")]
        public List<string> SupportedFunctionsExtensionVersions { get; set; }

        [JsonProperty("isHidden")]
        public bool? IsHidden { get; set; }

        [JsonProperty("isEarlyAccess")]
        public bool? IsEarlyAccess { get; set; }

        [JsonProperty("endOfLifeDate")]
        public DateTime? EndOfLifeDate { get; set; }

        [JsonProperty("isDefault")]
        public bool? IsDefault { get; set; }

        [JsonProperty("isDeprecated")]
        public bool? IsDeprecated { get; set; }

        [JsonProperty("isPreview")]
        public bool? IsPreview { get; set; }

        [JsonProperty("isAutoUpdate")]
        public bool? IsAutoUpdate { get; set; }
    }

    public class LinuxRuntimeSettings
    {
        [JsonProperty("runtimeVersion")]
        public string RuntimeVersion { get; set; }

        [JsonProperty("isHidden")]
        public bool IsHidden { get; set; }

        [JsonProperty("IsEarlyAccess")]
        public bool isEarlyAccess { get; set; }

        [JsonProperty("RemoteDebuggingSupported")]
        public bool remoteDebuggingSupported { get; set; }

        [JsonProperty("appInsightsSettings")]
        public AppInsightsSettings AppInsightsSettings { get; set; }

        [JsonProperty("gitHubActionSettings")]
        public GitHubActionSettings GitHubActionSettings { get; set; }

        [JsonProperty("appSettingsDictionary")]
        public AppSettingsDictionary AppSettingsDictionary { get; set; }

        [JsonProperty("siteConfigPropertiesDictionary")]
        public SiteConfigPropertiesDictionary SiteConfigPropertiesDictionary { get; set; }

        [JsonProperty("supportedFunctionsExtensionVersions")]
        public List<string> SupportedFunctionsExtensionVersions { get; set; }

        [JsonProperty("endOfLifeDate")]
        public DateTime EndOfLifeDate { get; set; }

        [JsonProperty("isDefault")]
        public bool? IsDefault { get; set; }

        [JsonProperty("IsDeprecated")]
        public bool? isDeprecated { get; set; }

        [JsonProperty("IsPreview")]
        public bool? isPreview { get; set; }

        [JsonProperty("IsAutoUpdate")]
        public bool? isAutoUpdate { get; set; }
    }

    public class AppInsightsSettings
    {
        [JsonProperty("isSupported")]
        public bool IsSupported { get; set; }
    }

    public class GitHubActionSettings
    {
        [JsonProperty("IsSupported")]
        public bool isSupported { get; set; }

        [JsonProperty("SupportedVersion")]
        public string supportedVersion { get; set; }
    }

    public class AppSettingsDictionary
    {
        [JsonProperty("FUNCTIONS_WORKER_RUNTIME")]
        public string FUNCTIONS_WORKER_RUNTIME { get; set; }

        [JsonProperty("WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED")]
        public string WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED { get; set; }

        [JsonProperty("WEBSITE_NODE_DEFAULT_VERSION")]
        public string WEBSITE_NODE_DEFAULT_VERSION { get; set; }
    }

    public class SiteConfigPropertiesDictionary
    {
        [JsonProperty("use32BitWorkerProcess")]
        public bool Use32BitWorkerProcess { get; set; }

        [JsonProperty("netFrameworkVersion")]
        public string NetFrameworkVersion { get; set; }

        [JsonProperty("javaVersion")]
        public string JavaVersion { get; set; }

        [JsonProperty("powerShellVersion")]
        public string PowerShellVersion { get; set; }

        [JsonProperty("linuxFxVersion")]
        public string LinuxFxVersion { get; set; }
    }
}
