// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsite
    {
        public IEnumerable<string> EnabledHostNames { get; set; }

        public string Sku { get; set; }

        public FunctionAppConfig FunctionAppConfig { get; set; }
    }

    public class Authentication
    {
        public string Type { get; set; }

        public object UIserAssignedIdentityResourceId { get; set; }

        public string StorageAccountConnectionStringName { get; set; }
    }

    public class Deployment
    {
        public Storage Storage { get; set; }
    }

    public class FunctionAppConfig
    {
        public Deployment Deployment { get; set; }

        public Runtime Runtime { get; set; }

        public ScaleAndConcurrency ScaleAndConcurrency { get; set; }
    }

    public class Runtime
    {
        public string Name { get; set; }

        public string Version { get; set; }
    }

    public class ScaleAndConcurrency
    {
        public List<object> AlwaysReady { get; set; }

        public int MaximumInstanceCount { get; set; }

        public int InstanceMemoryMB { get; set; }

        public object Triggers { get; set; }
    }

    public class Storage
    {
        public string Type { get; set; }

        public string Value { get; set; }

        public Authentication Authentication { get; set; }
    }
}
