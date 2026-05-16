// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Release channel for the Functions extension bundle. Values map to the
/// three published bundle ids (GA / Preview / Experimental).
/// </summary>
internal enum BundleChannel
{
    GA,
    Preview,
    Experimental,
}

internal static class BundleIds
{
    public static string For(BundleChannel channel) => channel switch
    {
        BundleChannel.Preview => "Microsoft.Azure.Functions.ExtensionBundle.Preview",
        BundleChannel.Experimental => "Microsoft.Azure.Functions.ExtensionBundle.Experimental",
        _ => "Microsoft.Azure.Functions.ExtensionBundle",
    };
}
