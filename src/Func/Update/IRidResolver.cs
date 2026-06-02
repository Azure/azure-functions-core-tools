// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Resolves the current process's .NET RID (e.g. <c>win-x64</c>, <c>osx-arm64</c>)
/// and picks the matching <see cref="ReleaseAsset"/> from a <see cref="Release"/>.
/// Exists as an interface so <c>UpdateCommand</c> can be unit-tested without
/// depending on the test host's actual OS/architecture.
/// </summary>
internal interface IRidResolver
{
    /// <summary>
    /// Returns the RID for the running process.
    /// </summary>
    /// <exception cref="Common.GracefulException">
    /// Thrown when the OS or process architecture is not one of the six
    /// platforms the func CLI ships binaries for.
    /// </exception>
    public string GetCurrentRid();

    /// <summary>
    /// Picks the asset on <paramref name="release"/> that matches
    /// <paramref name="rid"/>. Returns <c>null</c> when no asset matches; the
    /// caller decides whether that's a fatal error or a "no build for this
    /// platform" no-op.
    /// </summary>
    public ReleaseAsset? SelectAsset(Release release, string rid);
}
