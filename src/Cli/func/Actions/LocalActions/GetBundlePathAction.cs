// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "path", Context = Context.Bundles, HelpText = "Get the path to the downloaded extension bundle.")]
    internal class GetBundlePathAction : BaseAction
    {
        public string Language { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
            if (extensionBundleManager.IsExtensionBundleConfigured())
            {
                try
                {
                    var options = ExtensionBundleHelper.GetExtensionBundleOptions();
                    var bundleBasePath = ExtensionBundleHelper.GetBundleDownloadPath(options.Id);
                    var bundleDetails = await extensionBundleManager.GetExtensionBundleDetails();
                    var bundlePath = Path.Combine(bundleBasePath, bundleDetails.Version);

                    if (string.IsNullOrEmpty(bundlePath))
                    {
                        throw new CliException("Unable to locate extension bundle.");
                    }
                    else
                    {
                        ColoredConsole.WriteLine(bundlePath);
                    }
                }
                catch (Exception e)
                {
                    throw new CliException("Unable to locate extension bundle.", e);
                }
            }
            else
            {
                ColoredConsole.WriteLine("Extension bundle not configured.");
            }
        }
    }
}
