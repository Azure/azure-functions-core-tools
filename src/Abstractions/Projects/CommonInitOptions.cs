// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Factories for <see cref="Option"/>s that multiple workloads contribute to <c>func init</c>.
/// Centralising the name, description, default, and alias here keeps wording consistent
/// across stacks and lets <see cref="IInitOptionRegistry"/> share a single canonical instance
/// per <c>InitCommand</c>.
/// </summary>
/// <remarks>
/// Each method returns a fresh instance per call. The registry collapses duplicates so the
/// option appears once in <c>--help</c> and every workload sees the same parsed value.
/// </remarks>
public static class CommonInitOptions
{
    /// <summary>
    /// <c>--no-bundles</c>. Used by every stack that emits a default extension bundle entry
    /// in <c>host.json</c> (Node, Python, Go, ...).
    /// </summary>
    public static Option<bool> NoBundle() => new("--no-bundles")
    {
        Description = "Skip writing the default extensionBundle block in host.json.",
        DefaultValueFactory = _ => false,
    };

    /// <summary>
    /// <c>--bundles-channel</c> / <c>-c</c>. Selects the extension bundle release channel
    /// (<see cref="BundleChannel.GA"/> by default).
    /// </summary>
    public static Option<BundleChannel> BundlesChannel() => new("--bundles-channel", "-c")
    {
        Description = "Extension bundle release channel: GA (default), Preview, or Experimental.",
        DefaultValueFactory = _ => BundleChannel.GA,
    };
}
