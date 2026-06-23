// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Sdk.Tests;

public static class TestHelpers
{
#if DEBUG
    public const string Configuration = "debug";
#else
    public const string Configuration = "release";
#endif

    public static string GetProjectAssemblyPath(string projectName)
    {
        return Path.GetFullPath($@"../../{projectName}/{Configuration}/Azure.Functions.Cli.{projectName}.dll");
    }
}
