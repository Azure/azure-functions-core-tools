// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "path", Context = Context.Bundles, HelpText = "Get the path to the downloaded extension bundle.")]
    internal class GetBundlePathAction : BaseAction
    {
        public override async Task RunAsync()
        {
            if (!BundleActionHelper.TryGetBundleContext(out var extensionBundleManager, out _, out var bundleBasePath))
            {
                ColoredConsole.WriteLine("Extension bundle not configured.");
                return;
            }

            try
            {
                var bundleDetails = await extensionBundleManager.GetExtensionBundleDetails();
                var bundlePath = Path.Combine(bundleBasePath, bundleDetails.Version);
                ColoredConsole.WriteLine(bundlePath);
            }
            catch (Exception e)
            {
                throw new CliException("Unable to locate extension bundle.", e);
            }
        }
    }
}
