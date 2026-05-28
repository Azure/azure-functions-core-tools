// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Helper methods for bundle-related operations.
/// </summary>
internal static class BundleHelpers
{
    public const string StableBundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    public const string PreviewBundleId = "Microsoft.Azure.Functions.ExtensionBundle.Preview";
    public const string ExperimentalBundleId = "Microsoft.Azure.Functions.ExtensionBundle.Experimental";

    public const string PreviewLabel = "preview";
    public const string ExperimentalLabel = "experimental";

    private static readonly Dictionary<string, BundleChannel> _bundleIdToChannelMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [StableBundleId] = BundleChannel.Stable,
            [PreviewBundleId] = BundleChannel.Preview,
            [ExperimentalBundleId] = BundleChannel.Experimental
        };

    private static readonly Dictionary<string, BundleChannel> _releaseToChannelMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [PreviewLabel] = BundleChannel.Preview,
            [ExperimentalLabel] = BundleChannel.Experimental
        };

    /// <summary>
    /// Determines the channel of a bundle based on its ID.
    /// </summary>
    /// <param name="bundleId">The bundle ID.</param>
    /// <returns>The channel of the bundle.</returns>
    public static BundleChannel GetBundleChannel(string bundleId)
    {
        if (_bundleIdToChannelMap.TryGetValue(bundleId, out BundleChannel channel))
        {
            return channel;
        }

        throw new ArgumentException("Invalid bundle ID.", nameof(bundleId));
    }

    /// <summary>
    /// Determines the channel of a bundle based on its version.
    /// </summary>
    /// <param name="version">The nuget package version.</param>
    /// <returns>The channel of the bundle.</returns>
    public static BundleChannel GetBundleChannel(NuGetVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!version.IsPrerelease)
        {
            // shortcut for optimization.
            return BundleChannel.Stable;
        }

        // Prerelease check above ensures that the version has a label.
        string label = version.ReleaseLabels.FirstOrDefault()!;
        if (_releaseToChannelMap.TryGetValue(label, out BundleChannel channel))
        {
            return channel;
        }

        return BundleChannel.Stable; // no matching label, assume stable.
    }
}
