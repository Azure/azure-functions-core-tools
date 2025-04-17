// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsiteConfig
    {
        public string ScmType { get; set; }

        public string LinuxFxVersion { get; set; }

        public string NetFrameworkVersion { get; set; }
    }
}
