// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.FileSystemGlobbing;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Default <see cref="IDirectoryMarkerMatcher"/> backed by
/// <see cref="Matcher"/>. Walks the directory once per call. The resolver
/// invokes this per detector, so for typical "5 detectors, ~10 markers each"
/// installs the cost stays well under a second on warm cache.
/// </summary>
internal sealed class DirectoryMarkerMatcher : IDirectoryMarkerMatcher
{
    public bool AnyMatch(DirectoryInfo directory, IReadOnlyList<string> markers)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(markers);

        // Empty marker list = always candidate, per spec §5.2 pre-filter.
        if (markers.Count == 0)
        {
            return true;
        }

        if (!directory.Exists)
        {
            return false;
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(markers);
        return matcher.Execute(new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(directory)).HasMatches;
    }
}
