// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.Helpers
{
    internal class ResolveBuildOptionHelper
    {
        public static BuildOption ResolveBuildOption(BuildOption currentBuildOption, WorkerRuntime runtime, Site site, bool buildNativeDeps, bool noBuild, bool includeLocalBuildForWindows = false)
        {
            // --no-build and --build-native-deps will take precedence over --build local and --build remote
            // Note that --build local and --build remote are options only for publishing the function app, not packing
            if (noBuild)
            {
                return BuildOption.None;
            }

            if (buildNativeDeps)
            {
                return BuildOption.Container;
            }

            if (currentBuildOption == BuildOption.Default)
            {
                // Change to remote build if, python app, has requirements.txt, requirements.txt has content
                if (runtime == WorkerRuntime.Python &&
                    FileSystemHelpers.FileExists(Constants.RequirementsTxt) &&
                    new FileInfo(Path.Combine(Environment.CurrentDirectory, Constants.RequirementsTxt)).Length > 0)
                {
                    // Include default (local) build option for Windows as some customers may be using a local build for testing purposes
                    // Note that this will be deprecated in the future after 4.1.1 is released
                    if (includeLocalBuildForWindows && OperatingSystem.IsWindows())
                    {
                        ColoredConsole.WriteLine("Python runtime detected on Windows. Using local build option.");
                        ColoredConsole.WriteLine(OutputTheme.WarningColor($"The default build option for python function apps with a valid requirements.txt " +
                            $"will be switched over to use remote builds after version 4.1.1.\n" +
                            "If a local build is still needed, please use the `--build-local` flag when running `func pack`."));
                        return BuildOption.Default;
                    }

                    return BuildOption.Remote;
                }
            }

            return currentBuildOption;
        }
    }
}
