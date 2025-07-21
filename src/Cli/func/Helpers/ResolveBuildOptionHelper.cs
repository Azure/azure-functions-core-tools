// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    internal class ResolveBuildOptionHelper
    {
        public static BuildOption ResolveBuildOption(BuildOption currentBuildOption, WorkerRuntime runtime, Site site, bool buildNativeDeps, bool noBuild)
        {
            // --no-build and --build-native-deps will take precedence over --build local and --build remote
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
                    return BuildOption.Remote;
                }
            }

            return currentBuildOption;
        }
    }
}
