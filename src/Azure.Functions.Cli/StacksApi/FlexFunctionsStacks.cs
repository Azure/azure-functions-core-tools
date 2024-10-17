using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.StacksApi
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);

    public class FlexFunctionsStacks
    {
        [JsonProperty("value")]
        public List<FlexLanguage> Languages { get; set; }

        [JsonProperty("nextLink")]
        public object NextLink { get; set; }

        [JsonProperty("id")]
        public object Id { get; set; }
    }

    public class FlexLanguage
    {
        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("properties")]
        public FlexLanguageProperties LanguageProperties { get; set; }
    }

    public class FlexLanguageProperties
    {
        [JsonProperty("displayText")]
        public string DisplayText { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("preferredOs")]
        public string PreferredOs { get; set; }

        [JsonProperty("majorVersions")]
        public List<FlexMajorVersion> MajorVersions { get; set; }
    }

    public class FlexMajorVersion
    {
        [JsonProperty("displayText")]
        public string DisplayText { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("minorVersions")]
        public List<FlexMinorVersion> MinorVersions { get; set; }
    }

    public class FlexMinorVersion
    {
        [JsonProperty("displayText")]
        public string DisplayText { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("stackSettings")]
        public FlexStackSettings StackSettings { get; set; }
    }

    public class FlexStackSettings
    {
        [JsonProperty("linuxRuntimeSettings")]
        public FlexLinuxRuntimeSettings LinuxRuntimeSettings { get; set; }
    }

    public class FlexLinuxRuntimeSettings
    {
        [JsonProperty("runtimeVersion")]
        public string RuntimeVersion { get; set; }

        [JsonProperty("remoteDebuggingSupported")]
        public bool RemoteDebuggingSupported { get; set; }

        [JsonProperty("isPreview")]
        public bool IsPreview { get; set; }

        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; }

        [JsonProperty("isHidden")]
        public bool IsHidden { get; set; }

        [JsonProperty("appInsightsSettings")]
        public FlexAppInsightsSettings AppInsightsSettings { get; set; }

        [JsonProperty("gitHubActionSettings")]
        public FlexGitHubActionSettings GitHubActionSettings { get; set; }

        [JsonProperty("appSettingsDictionary")]
        public FlexAppSettingsDictionary AppSettingsDictionary { get; set; }

        [JsonProperty("siteConfigPropertiesDictionary")]
        public FlexSiteConfigPropertiesDictionary SiteConfigPropertiesDictionary { get; set; }

        [JsonProperty("supportedFunctionsExtensionVersions")]
        public List<string> SupportedFunctionsExtensionVersions { get; set; }

        [JsonProperty("supportedFunctionsExtensionVersionsInfo")]
        public List<FlexSupportedFunctionsExtensionVersionsInfo> SupportedFunctionsExtensionVersionsInfo { get; set; }

        [JsonProperty("endOfLifeDate")]
        public string EndOfLifeDate { get; set; }

        [JsonProperty("Sku")]
        public List<FlexSku> Sku { get; set; }
    }

    public class FlexAppInsightsSettings
    {
        [JsonProperty("isSupported")]
        public bool IsSupported { get; set; }
    }

    public class FlexGitHubActionSettings
    {
        [JsonProperty("isSupported")]
        public bool IsSupported { get; set; }

        [JsonProperty("supportedVersion")]
        public string SupportedVersion { get; set; }
    }

    public class FlexAppSettingsDictionary
    {
        [JsonProperty("FUNCTIONS_WORKER_RUNTIME")]
        public string FUNCTIONS_WORKER_RUNTIME { get; set; }
    }

    public class FlexSiteConfigPropertiesDictionary
    {
        [JsonProperty("use32BitWorkerProcess")]
        public bool Use32BitWorkerProcess { get; set; }

        [JsonProperty("linuxFxVersion")]
        public string LinuxFxVersion { get; set; }
    }

    public class FlexSupportedFunctionsExtensionVersionsInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("isDeprecated")]
        public bool IsDeprecated { get; set; }

        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; }
    }

    public class FlexSku
    {
        [JsonIgnore]
        public bool IsDefault { get; set; }

        [JsonProperty("skuCode")]
        public string SkuCode { get; set; }

        [JsonProperty("instanceMemoryMB")]
        public List<FlexInstanceMemoryMB> InstanceMemoryMB { get; set; }

        [JsonProperty("maximumInstanceCount")]
        public FlexMaximumInstanceCount MaximumInstanceCount { get; set; }

        [JsonProperty("FunctionAppConfigProperties")]
        public FunctionAppConfigProperties functionAppConfigProperties { get; set; }
    }

    public class FunctionAppConfigProperties
    {
        [JsonProperty("runtime")]
        public FlexRuntime Runtime { get; set; }
    }

    public class FlexInstanceMemoryMB
    {
        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; }
    }

    public class FlexMaximumInstanceCount
    {
        [JsonProperty("lowestMaximumInstanceCount")]
        public int LowestMaximumInstanceCount { get; set; }

        [JsonProperty("highestMaximumInstanceCount")]
        public int HighestMaximumInstanceCount { get; set; }

        [JsonProperty("defaultValue")]
        public int DefaultValue { get; set; }
    }

    public class FlexRuntime
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }
}
