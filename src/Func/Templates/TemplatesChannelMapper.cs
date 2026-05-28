// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
    public const string StableLabel = "";
    public const string PreviewLabel = "preview";
    public const string ExperimentalLabel = "experimental";

    public const string StableBundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    public const string PreviewBundleId = "Microsoft.Azure.Functions.ExtensionBundle.Preview";
    public const string ExperimentalBundleId = "Microsoft.Azure.Functions.ExtensionBundle.Experimental";

    /// <summary>
    /// Returns the templates channel prerelease label for the supplied
    /// <c>host.json</c> bundle id, or <c>null</c> when the id is not one of
    /// the three recognised channels.
    /// </summary>
    public static string? GetChannelLabel(string? bundleId)
    {
        if (string.IsNullOrWhiteSpace(bundleId))
        {
            return null;
        }

        return bundleId.Trim() switch
        {
            string s when s.Equals(StableBundleId, StringComparison.OrdinalIgnoreCase) => StableLabel,
            string s when s.Equals(PreviewBundleId, StringComparison.OrdinalIgnoreCase) => PreviewLabel,
            string s when s.Equals(ExperimentalBundleId, StringComparison.OrdinalIgnoreCase) => ExperimentalLabel,
            _ => null,
        };
    }

    /// <summary>
    /// Returns a human-readable channel name (<c>"stable"</c>, <c>"preview"</c>,
    /// <c>"experimental"</c>) for diagnostics / hint copy. The empty label
    /// for stable is rendered as <c>"stable"</c>.
    /// </summary>
    public static string GetChannelDisplayName(string channelLabel) =>
        string.IsNullOrEmpty(channelLabel) ? "stable" : channelLabel;

    /// <summary>
    /// Extracts the prerelease label from a workload pkg version (e.g.
    /// <c>"1.0.0-preview"</c> → <c>"preview"</c>, <c>"1.0.0"</c> → empty).
    /// Case-insensitive on the prerelease part. The label is compared
    /// against <see cref="GetChannelLabel"/> output.
    /// </summary>
    public static string GetPrereleaseLabel(string packageVersion)
    {
        if (string.IsNullOrWhiteSpace(packageVersion))
        {
            return string.Empty;
        }

        int dash = packageVersion.IndexOf('-');
        if (dash < 0 || dash == packageVersion.Length - 1)
        {
            return string.Empty;
        }

        string suffix = packageVersion[(dash + 1)..];
        // Trim build metadata after '+' per SemVer.
        int plus = suffix.IndexOf('+');
        if (plus >= 0)
        {
            suffix = suffix[..plus];
        }

        return suffix.ToLowerInvariant();
    }

    /// <summary>
    /// Picks the channel-matched installed workload row whose prerelease
    /// label equals <paramref name="channelLabel"/>. Returns the highest
    /// matching version (lex-sorted on package version string — sufficient
    /// for PR4; PR5 can switch to NuGetVersion comparison if needed).
    /// Returns <c>null</c> when no row matches.
    /// </summary>
    public static InstalledTemplatesWorkload? PickChannelMatched(
        IReadOnlyList<InstalledTemplatesWorkload> rows,
        string channelLabel)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(channelLabel);

        InstalledTemplatesWorkload? best = null;
        foreach (InstalledTemplatesWorkload row in rows)
        {
            string rowLabel = GetPrereleaseLabel(row.PackageVersion);
            if (!string.Equals(rowLabel, channelLabel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best is null
                || string.Compare(row.PackageVersion, best.PackageVersion, StringComparison.Ordinal) > 0)
            {
                best = row;
            }
        }

        return best;
    }
}
