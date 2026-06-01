// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Maps between the project's <c>host.json</c> <c>extensionBundle.id</c>,
/// the corresponding templates channel name, and the prerelease label a
/// channel-matched templates workload pkg version uses (templates-workload-spec.md
/// §4.4.2; func-new spec §4.8.1).
/// </summary>
/// <remarks>
/// Channel mapping is deliberately implicit — there is no
/// <c>--templates-channel</c> flag (§4.8.1). The mapping table is small;
/// keeping it here lets every consumer share one source of truth.
/// </remarks>
internal static class TemplatesChannelMapper
{
    /// <summary>
    /// Picks the channel-matched installed workload row whose prerelease
    /// label equals <paramref name="channelLabel"/>. Returns the highest
    /// matching version (lex-sorted on package version string — sufficient
    /// for PR4; PR5 can switch to NuGetVersion comparison if needed).
    /// Returns <c>null</c> when no row matches.
    /// </summary>
    /// <param name="rows">The list of installed templates workloads to search.</param>
    /// <param name="channel">The channel to match.</param>
    /// <returns>The matching installed templates workload, or <c>null</c> if none found.</returns>
    public static InstalledTemplatesWorkload? PickChannelMatched(
        IReadOnlyList<InstalledTemplatesWorkload> rows, BundleChannel channel)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (channel == BundleChannel.Unknown)
        {
            return null;
        }

        InstalledTemplatesWorkload? best = null;
        NuGetVersion? bestVersion = null;
        foreach (InstalledTemplatesWorkload row in rows)
        {
            NuGetVersion rowVersion = new(row.PackageVersion);
            BundleChannel matched = BundleHelpers.GetBundleChannel(rowVersion);
            if (matched != channel)
            {
                continue;
            }

            if (best is null || rowVersion.CompareTo(bestVersion) > 0)
            {
                best = row;
                bestVersion = rowVersion;
            }
        }

        return best;
    }
}
