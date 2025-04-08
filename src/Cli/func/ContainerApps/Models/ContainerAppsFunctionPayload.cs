﻿using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.ContainerApps.Models
{
    public class ContainerAppsFunctionPayload
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "location")]
        public string Location { get; set; }

        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; } = "Microsoft.Web/sites";

        [JsonProperty(PropertyName = "kind")]
        public string Kind { get; set; } = "functionapp";

        [JsonProperty(PropertyName = "properties")]
        public ContainerAppsFunctionProperties Properties { get; set; }

        public class ContainerAppsFunctionProperties
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "managedEnvironmentId")]
            public string ManagedEnvironmentId { get; set; }

            [JsonProperty(PropertyName = "siteConfig")]
            public ContainerAppsFunctionSiteConfig SiteConfig { get; set; }
        }

        public class ContainerAppsFunctionSiteConfig
        {
            [JsonProperty(PropertyName = "linuxFxVersion")]
            public string LinuxFxVersion { get; set; }

            [JsonProperty(PropertyName = "appSettings")]
            public List<ContainerAppsFunctionAppSettings> AppSettings { get; set; }

            [JsonProperty(PropertyName = "connectionStrings")]
            public List<ContainerAppsFunctionConnectionStrings> ConnectionStrings { get; set; }
        }

        public class ContainerAppsFunctionAppSettings
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
        }

        public class ContainerAppsFunctionConnectionStrings
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "connectionString")]
            public string ConnectionString { get; set; }

            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }
        }

        private ContainerAppsFunctionPayload(string name, string location, string managedEnvironmentId, string linuxFxVersion, List<ContainerAppsFunctionAppSettings> appSettings, List<ContainerAppsFunctionConnectionStrings> connectionStrings)
        {
            Name = name;
            Location = location;
            Properties = new ContainerAppsFunctionProperties
            {
                Name = name,
                ManagedEnvironmentId = managedEnvironmentId,
                SiteConfig = new ContainerAppsFunctionSiteConfig
                {
                    LinuxFxVersion = linuxFxVersion,
                    AppSettings = appSettings,
                    ConnectionStrings = connectionStrings
                }
            };
        }

        public static ContainerAppsFunctionPayload CreateInstance(string name, string location, string managedEnvironmentId, string linuxFxVersion, string storageConnection, string runtime, string registry, string registryUsername, string registryPassword, Dictionary<string, string> appSettings = null, List<ContainerAppsFunctionConnectionStrings> connectionStrings = null)
        {
            appSettings ??= new Dictionary<string, string>();
            connectionStrings ??= new List<ContainerAppsFunctionConnectionStrings>();

            appSettings["AzureWebJobsStorage"] = storageConnection;
            appSettings["FUNCTIONS_WORKER_RUNTIME"] = runtime;

            if (!string.IsNullOrEmpty(registry))
            {
                appSettings["DOCKER_REGISTRY_SERVER_URL"] = registry;
            }

            if (!string.IsNullOrEmpty(registryUsername))
            {
                appSettings["DOCKER_REGISTRY_SERVER_USERNAME"] = registryUsername;
                appSettings["DOCKER_REGISTRY_SERVER_PASSWORD"] = registryPassword;
            }

            var allAppSettings = appSettings.Select(kvp => new ContainerAppsFunctionAppSettings { Name = kvp.Key, Value = kvp.Value }).ToList();
            return new ContainerAppsFunctionPayload(name, location, managedEnvironmentId, linuxFxVersion, allAppSettings, connectionStrings);
        }
    }
}
