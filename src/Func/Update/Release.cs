// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Semver;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// A published func CLI release as seen by the update pipeline. The version
/// drives both quality classification (stable vs preview) and "newer than
/// current" comparisons. The download URL is constructed from the CDN base
/// and the version/RID.
/// </summary>
internal sealed record Release(SemVersion Version, Uri DownloadUrl)
{
    /// <summary>
    /// The archive file extension for the current OS: <c>"zip"</c> on Windows,
    /// <c>"tar.gz"</c> on Linux and macOS.
    /// </summary>
    internal static string ArchiveExtension { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "zip" : "tar.gz";

    /// <summary>
    /// Expected SHA-256 hex digest of the downloaded archive, or <c>null</c>
    /// when the release feed does not yet publish checksums.
    /// </summary>
    // TODO: Populate from the release feed once checksum metadata is available (#5445).
    public string? Sha256Checksum { get; init; }

    public bool IsPrerelease => Version.IsPrerelease;
}
