// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Release channel for the Functions extension bundle. Values map to the
/// three published bundle ids (GA / Preview / Experimental).
/// </summary>
public enum BundleChannel
{
    GA,
    Preview,
    Experimental,
}

/// <summary>
/// Shared metadata for the Functions extension bundle. Used by script-based
/// workloads (Python, Node, ...) that don't compile in their own extensions.
/// </summary>
public static class ExtensionBundle
{
    /// <summary>
    /// Default version range pulled by <c>func init</c> templates.
    /// </summary>
    public const string DefaultVersionRange = "[4.*, 5.0.0)";

    /// <summary>
    /// Returns the NuGet id for the bundle published under <paramref name="channel"/>.
    /// </summary>
    public static string IdFor(BundleChannel channel) => channel switch
    {
        BundleChannel.Preview => "Microsoft.Azure.Functions.ExtensionBundle.Preview",
        BundleChannel.Experimental => "Microsoft.Azure.Functions.ExtensionBundle.Experimental",
        _ => "Microsoft.Azure.Functions.ExtensionBundle",
    };
}
