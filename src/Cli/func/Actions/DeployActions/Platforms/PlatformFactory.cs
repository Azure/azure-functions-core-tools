// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms
{
    public static class PlatformFactory
    {
        public static IHostingPlatform CreatePlatform(string name)
        {
            return name switch
            {
                "kubernetes" => new KubernetesPlatform(),
                "knative" => new KnativePlatform(),
                _ => null,
            };
        }
    }
}
