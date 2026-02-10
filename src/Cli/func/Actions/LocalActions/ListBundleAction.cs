// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "list", Context = Context.Bundles, HelpText = "List downloaded extension bundles.")]
    internal class ListBundleAction : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var (success, extensionBundleManager, options, bundleBasePath) = await BundleActionHelper.TryGetBundleContextAsync();
            if (!success)
            {
                ColoredConsole.WriteLine("To list downloaded bundles, you must configure extension bundles in your host.json.");
                return;
            }

            try
            {
                if (!Directory.Exists(bundleBasePath))
                {
                    ColoredConsole.WriteLine($"No bundles downloaded yet.");
                    ColoredConsole.WriteLine($"Expected location: {bundleBasePath}");
                    ColoredConsole.WriteLine($"Run 'func bundle download' to download the configured extension bundle.");
                    return;
                }

                // Get all version directories
                var versionDirectories = Directory.GetDirectories(bundleBasePath);

                if (versionDirectories.Length == 0)
                {
                    ColoredConsole.WriteLine($"No bundles found at: {bundleBasePath}");
                    ColoredConsole.WriteLine($"Run 'func bundle download' to download the configured extension bundle.");
                    return;
                }

                ColoredConsole.WriteLine($"Available extension bundles:");
                ColoredConsole.WriteLine();

                foreach (var versionDir in versionDirectories.OrderByDescending(d => d))
                {
                    var version = Path.GetFileName(versionDir);
                    var dirInfo = new DirectoryInfo(versionDir);

                    // Check if directory has files
                    var fileCount = Directory.GetFileSystemEntries(versionDir).Length;

                    if (fileCount > 0)
                    {
                        ColoredConsole.WriteLine($"  {AdditionalInfoColor(options.Id)} {VerboseColor("v" + version)}");
                        ColoredConsole.WriteLine($"    Last Modified: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                        ColoredConsole.WriteLine($"    Location: {versionDir}");
                        ColoredConsole.WriteLine();
                    }
                }

                // Show the currently configured version range
                ColoredConsole.WriteLine($"Configured version range in host.json: {VerboseColor(options.Version.ToString())}");
            }
            catch (Exception ex)
            {
                throw new CliException($"Failed to list extension bundles: {ex.Message}", ex);
            }

            await Task.CompletedTask;
        }
    }
}
