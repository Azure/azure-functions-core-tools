// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.Helpers
{
    internal class ResolveBuildOptionHelper
    {
        public static BuildOption ResolveBuildOption(BuildOption currentBuildOption, WorkerRuntime runtime, Site site, bool buildNativeDeps, bool noBuild, bool isFuncPackAction = false, bool buildOptionLocal = false)
        {
            // --no-build, --build-native-deps, and --build-local will take precedence over --build local and --build remote
            // Note that --build local and --build remote are options only for publishing the function app, not packing
            if (noBuild)
            {
                return BuildOption.None;
            }

            if (buildNativeDeps)
            {
                return BuildOption.Container;
            }

            if (buildOptionLocal)
            {
                return BuildOption.Local;
            }

            if (currentBuildOption == BuildOption.Default || (isFuncPackAction && currentBuildOption == BuildOption.Local))
            {
                // Change to remote build if, python app, has requirements.txt, requirements.txt has content
                if (runtime == WorkerRuntime.Python &&
                    FileSystemHelpers.FileExists(Constants.RequirementsTxt) &&
                    new FileInfo(Path.Combine(Environment.CurrentDirectory, Constants.RequirementsTxt)).Length > 0)
                {
                    // Include default (local) build option for Windows as some customers may be using a local build for testing purposes
                    // Note that this will be deprecated in the future after 4.1.1 is released
                    if (isFuncPackAction && OperatingSystem.IsWindows())
                    {
                        ColoredConsole.WriteLine("Python runtime detected on Windows. Using local build option.");
                        ColoredConsole.WriteLine(OutputTheme.WarningColor($"The default build option for python function apps with a valid requirements.txt " +
                            $"will be switched over to use remote builds after version 4.1.1.\n" +
                            "If a local build is still needed, please use the `--build-local` flag when running `func pack`."));
                        return BuildOption.Local;
                    }
                    else if (isFuncPackAction && !OperatingSystem.IsWindows())
                    {
                        ColoredConsole.WriteLine("Skipping local build...");
                        ColoredConsole.WriteLine("Python function app projects on non-Windows platforms default to a deferred build that is remote build ready." +
                            " Please perform a remote build when deploying.");
                    }

                    return isFuncPackAction ? BuildOption.Deferred : BuildOption.Remote;
                }
            }

            return currentBuildOption;
        }
    }
}
