// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.TestFramework
{
    public static class DotnetCompatibilityRunner
    {
        public static async Task Main(string[] args)
        {
            Directory.SetCurrentDirectory(args.Single());
            var framework = await DotnetHelpers.DetermineTargetFrameworkAsync(Environment.CurrentDirectory);
            Console.WriteLine($"Detected target framework: {framework}");

            // Exercise the Azure publish build path without contacting Azure or changing the test host's SDK.
            await DotnetHelpers.BuildDotnetProject(Path.Combine("bin", "publish"), "--configuration Release");
        }
    }
}
