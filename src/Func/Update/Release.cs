// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Semver;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// A published func CLI release as seen by the update pipeline. Parsed from a
/// GitHub release; the version comes from the release tag and drives both
/// channel classification and "newer than current" comparisons.
/// </summary>
internal sealed record Release(
    SemVersion Version,
    bool IsPrerelease,
    string TagName,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// A single downloadable artifact attached to a <see cref="Release"/>. The next
/// PR in the update chain matches one of these by RID.
/// </summary>
/// <remarks>
/// <see cref="Sha256"/> is plumbed nullable because the canonical hash source
/// hasn't been pinned down yet (see PR open questions). The install-script
/// team owns confirming it; this field is wired so downloaders can adopt it
/// without a record-shape change.
/// </remarks>
internal sealed record ReleaseAsset(
    string Name,
    string DownloadUrl,
    long Size,
    string? Sha256);
