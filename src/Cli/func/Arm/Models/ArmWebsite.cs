// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsite
    {
        [JsonProperty(PropertyName = "enabledHostNames")]
        public IEnumerable<string> EnabledHostNames { get; set; }

        [JsonProperty(PropertyName = "sku")]
        public string Sku { get; set; }

        [JsonProperty(PropertyName = "functionAppConfig")]
        public FunctionAppConfig FunctionAppConfig { get; set; }
    }

    public class Authentication
    {
        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }

        [JsonProperty(PropertyName = "userAssignedIdentityResourceId")]
        public object UserAssignedIdentityResourceId { get; set; }

        [JsonProperty(PropertyName = "storageAccountConnectionStringName")]
        public string StorageAccountConnectionStringName { get; set; }
    }

    public class Deployment
    {
        [JsonProperty(PropertyName = "storage")]
        public Storage Storage { get; set; }
    }

    public class FunctionAppConfig
    {
        [JsonProperty(PropertyName = "deployment")]
        public Deployment Deployment { get; set; }

        [JsonProperty(PropertyName = "runtime")]
        public Runtime Runtime { get; set; }

        [JsonProperty(PropertyName = "scaleAndConcurrency")]
        public ScaleAndConcurrency ScaleAndConcurrency { get; set; }
    }

    public class Runtime
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "version")]
        public string Version { get; set; }
    }

    public class ScaleAndConcurrency
    {
        [JsonProperty(PropertyName = "alwaysReady")]
        public List<object> AlwaysReady { get; set; }

        [JsonProperty(PropertyName = "maximumInstanceCount")]
        public int MaximumInstanceCount { get; set; }

        [JsonProperty(PropertyName = "instanceMemoryMB")]
        public int InstanceMemoryMB { get; set; }

        [JsonProperty(PropertyName = "triggers")]
        public object Triggers { get; set; }
    }

    public class Storage
    {
        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }

        [JsonProperty(PropertyName = "value")]
        public string Value { get; set; }

        [JsonProperty(PropertyName = "authentication")]
        public Authentication Authentication { get; set; }
    }
}
