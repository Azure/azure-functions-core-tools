// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
    public bool IsPrerelease => Version.IsPrerelease;
}
