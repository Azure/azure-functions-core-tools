// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
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

    public const string StableLabel = "stable";
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

    private static readonly Dictionary<string, BundleChannel> _labelToChannelMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [StableLabel] = BundleChannel.Stable,
            [PreviewLabel] = BundleChannel.Preview,
            [ExperimentalLabel] = BundleChannel.Experimental
        };

    /// <summary>
    /// Resolves a bundle identifier to its <see cref="BundleChannel"/>. Accepts
    /// either a full bundle id (e.g. <c>Microsoft.Azure.Functions.ExtensionBundle.Preview</c>)
    /// or a short channel label (<c>stable</c>, <c>preview</c>, <c>experimental</c>).
    /// </summary>
    /// <param name="idOrLabel">The full bundle id or short channel label.</param>
    /// <param name="channel">The resolved channel when the method returns <c>true</c>.</param>
    /// <returns><c>true</c> if <paramref name="idOrLabel"/> was recognized; otherwise <c>false</c>.</returns>
    public static bool TryResolveChannel(string idOrLabel, out BundleChannel channel)
    {
        if (!string.IsNullOrWhiteSpace(idOrLabel)
            && (_bundleIdToChannelMap.TryGetValue(idOrLabel, out channel)
                || _labelToChannelMap.TryGetValue(idOrLabel, out channel)))
        {
            return true;
        }

        channel = BundleChannel.Unknown;
        return false;
    }

    /// <summary>
    /// Converts a bundle channel to its display string.
    /// </summary>
    /// <param name="channel">The channel to convert.</param>
    /// <returns>The display string for the channel.</returns>
    public static string ToDisplayString(this BundleChannel channel) =>
        channel switch
        {
            BundleChannel.Unknown => "unknown",
            BundleChannel.Stable => StableLabel,
            BundleChannel.Preview => PreviewLabel,
            BundleChannel.Experimental => ExperimentalLabel,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
        };

    /// <summary>
    /// Returns the NuGet prerelease label that identifies <paramref name="channel"/>,
    /// or <c>null</c> for the stable channel (whose versions carry no prerelease label).
    /// </summary>
    /// <param name="channel">The channel to convert.</param>
    /// <returns>The prerelease label, or <c>null</c> for stable.</returns>
    public static string? ToPrereleaseLabel(this BundleChannel channel) =>
        channel switch
        {
            BundleChannel.Stable => null,
            BundleChannel.Preview => PreviewLabel,
            BundleChannel.Experimental => ExperimentalLabel,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
        };

    /// <summary>
    /// Determines whether <paramref name="version"/> belongs to <paramref name="channel"/>.
    /// Stable matches only released versions; preview and experimental match prerelease
    /// versions whose label identifies that channel.
    /// </summary>
    /// <param name="version">The nuget package version.</param>
    /// <param name="channel">The channel to test against.</param>
    /// <returns>True when the version belongs to the channel.</returns>
    public static bool MatchesChannel(NuGetVersion version, BundleChannel channel)
    {
        ArgumentNullException.ThrowIfNull(version);
        return channel == BundleChannel.Stable
            ? !version.IsPrerelease
            : version.IsPrerelease && GetBundleChannel(version) == channel;
    }

    /// <summary>
    /// Gets the channel label for a given bundle ID.
    /// </summary>
    /// <param name="bundleId">The bundle ID.</param>
    /// <param name="channelLabel">The channel label.</param>
    /// <returns>True if the channel label is found, false otherwise.</returns>
    public static bool TryGetBundleChannel(string bundleId, out BundleChannel channel)
    {
        if (_bundleIdToChannelMap.TryGetValue(bundleId, out channel))
        {
            return true;
        }

        channel = BundleChannel.Unknown;
        return false;
    }

    /// <summary>
    /// Determines the channel of a bundle based on its ID.
    /// </summary>
    /// <param name="bundleId">The bundle ID.</param>
    /// <returns>The channel of the bundle.</returns>
    public static BundleChannel GetBundleChannel(string bundleId)
    {
        if (TryGetBundleChannel(bundleId, out BundleChannel channel))
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
