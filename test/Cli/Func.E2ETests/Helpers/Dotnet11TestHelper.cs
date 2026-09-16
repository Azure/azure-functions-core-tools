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
                Assert.False(string.IsNullOrEmpty(root), "Install the .NET 11 SDK in a separate directory and set CORE_TOOLS_DOTNET11_ROOT.");
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

            // Do not replace DOTNET_ROOT: a framework-dependent Core Tools still needs .NET 10.
        }
    }
}
