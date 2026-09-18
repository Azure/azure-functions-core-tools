// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;

namespace Azure.Functions.Cli.E2ETests.Helpers
{
    internal static class Dotnet11TestHelper
    {
        internal static string DotnetPath
        {
            get
            {
                var root = Environment.GetEnvironmentVariable("CORE_TOOLS_DOTNET11_ROOT");

                // A developer box without a side-by-side .NET 11 SDK should skip these tests, not fail them.
                Skip.If(
                    string.IsNullOrEmpty(root),
                    "Install the .NET 11 SDK in a separate directory and set CORE_TOOLS_DOTNET11_ROOT to run the .NET 11 tests.");

                // Once the variable is set the SDK must actually be there. Failing (rather than skipping)
                // keeps a misconfigured pipeline from silently dropping this coverage.
                var path = Path.Combine(root!, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
                Assert.True(File.Exists(path), $"The .NET 11 SDK executable was not found at '{path}'.");
                return path;
            }
        }

        internal static void ConfigureCommand(FuncCommand command, string funcPath)
        {
            var dotnetPath = DotnetPath;
            command
                .WithEnvironmentVariable("PATH", string.Join(
                    Path.PathSeparator,
                    Path.GetDirectoryName(dotnetPath),
                    Path.GetDirectoryName(funcPath),
                    Environment.GetEnvironmentVariable("PATH")))
                .WithEnvironmentVariable("DOTNET_HOST_PATH", dotnetPath);

            // MSBuild exports these to child processes, pinning them to the SDK that is running the
            // tests (.NET 10), which cannot target net11.0 and fails with NETSDK1045. They take
            // precedence over PATH and DOTNET_HOST_PATH, so the SDK the func process spawns can only
            // be redirected by removing them.
            command.EnvironmentToRemove.Add("MSBuildSDKsPath");
            command.EnvironmentToRemove.Add("MSBuildExtensionsPath");
            command.EnvironmentToRemove.Add("MSBuildLoadMicrosoftTargetsReadOnly");

            // DOTNET_ROOT is deliberately left alone: func resolves its runtime from the
            // machine-wide install and ignores it, so overriding it here has no effect. That is
            // also why the start/run tests are skipped, since the worker is launched from a root
            // the harness cannot redirect. See
            // https://github.com/Azure/azure-functions-core-tools/issues/5602.
        }
    }
}
