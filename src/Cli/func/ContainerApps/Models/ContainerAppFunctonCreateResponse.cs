using Newtonsoft.Json;
using System;

namespace Azure.Functions.Cli.ContainerApps.Models
{
    internal class ContainerAppFunctonCreateResponse
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }

        [JsonProperty(PropertyName = "kind")]
        public string Kind { get; set; }

        [JsonProperty(PropertyName = "location")]
        public string Location { get; set; }

        [JsonProperty(PropertyName = "properties")]
        internal ContainerAppFunctonCreateResponseProperties Properties { get; set; }
    }

    internal class ContainerAppFunctonCreateResponseProperties
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "hostNames")]
        public string[] HostNames { get; set; }

        [JsonProperty(PropertyName = "webSpace")]
        public string WebSpace { get; set; }

        [JsonProperty(PropertyName = "repositorySiteName")]
        public string RepositorySiteName { get; set; }

        [JsonProperty(PropertyName = "usageState")]
        public string UsageState { get; set; }

        [JsonProperty(PropertyName = "enabledHostNames")]
        public string[] EnabledHostNames { get; set; }

        [JsonProperty(PropertyName = "availabilityState")]
        public string AvailabilityState { get; set; }

        [JsonProperty(PropertyName = "lastModifiedTimeUtc")]
        public DateTime LastModifiedTimeUtc { get; set; }

        [JsonProperty(PropertyName = "contentAvailabilityState")]
        public string ContentAvailabilityState { get; set; }

        [JsonProperty(PropertyName = "runtimeAvailabilityState")]
        public string RuntimeAvailabilityState { get; set; }

        [JsonProperty(PropertyName = "")]
        internal ContainerAppFunctonCreateResponseSiteconfig SiteConfig { get; set; }

        [JsonProperty(PropertyName = "deploymentId")]
        public string DeploymentId { get; set; }

        [JsonProperty(PropertyName = "kind")]
        public string Kind { get; set; }

        [JsonProperty(PropertyName = "managedEnvironmentId")]
        public string ManagedEnvironmentId { get; set; }

        [JsonProperty(PropertyName = "outboundIpAddresses")]
        public string OutboundIpAddresses { get; set; }

        [JsonProperty(PropertyName = "resourceGroup")]
        public string ResourceGroup { get; set; }

        [JsonProperty(PropertyName = "defaultHostName")]
        public string DefaultHostName { get; set; }

        [JsonProperty(PropertyName = "storageAccountRequired")]
        public bool StorageAccountRequired { get; set; }

        internal class ContainerAppFunctonCreateResponseSiteconfig
        {
            [JsonProperty(PropertyName = "linuxFxVersion")]
            public string LinuxFxVersion { get; set; }

            [JsonProperty(PropertyName = "windowsFxVersion")]
            public string WindowsFxVersion { get; set; }
        }
    }
}
