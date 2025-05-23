﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm.Models
{
    public class Site
    {
        public Site(string siteId)
        {
            SiteId = siteId;
        }

        public string SiteId { get; private set; }

        public string SiteName { get; set; }

        public string HostName { get; set; }

        public string Location { get; set; }

        public string ScmUri { get; set; }

        public string PublishingUserName { get; set; }

        public string PublishingPassword { get; set; }

        public string ScmType { get; set; }

        public string Kind { get; set; }

        public string Sku { get; set; }

        public string LinuxFxVersion { get; set; }

        public string NetFrameworkVersion { get; set; }

        public IDictionary<string, string> AzureAppSettings { get; set; }

        public IDictionary<string, AppServiceConnectionString> ConnectionStrings { get; set; }

        public bool IsLinux
            => Kind?.IndexOf("linux", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsDynamic
            => Sku?.Equals("dynamic", StringComparison.OrdinalIgnoreCase) == true;

        public bool IsFlex
            => Sku?.Equals("flexconsumption", StringComparison.OrdinalIgnoreCase) == true;

        public bool IsElasticPremium
            => Sku?.Equals("elasticpremium", StringComparison.OrdinalIgnoreCase) == true;

        public bool IsKubeApp
            => Kind?.IndexOf("kubernetes", StringComparison.OrdinalIgnoreCase) >= 0;

        public FunctionAppConfig FunctionAppConfig { get; set; }
    }
}
