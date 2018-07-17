using System;
using System.Collections.Generic;
using Azure.Functions.Cli.Actions.LocalActions.Platforms;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.DeployActions.Platforms
{
    public static class PlatformFactory
    {
        public static IHostingPlatform CreatePlatform(string name, string configFile = "")
        {
            switch (name)
            {
                case "kubernetes":
                    return new KubernetesPlatform(configFile);
                default:
                    return null;
            }
        }
    }
}