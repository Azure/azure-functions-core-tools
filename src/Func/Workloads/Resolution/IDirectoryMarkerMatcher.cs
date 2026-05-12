// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Evaluates a detector's <see cref="IProjectDetector.ProjectMarkers"/> globs
/// against a working directory. Behind an interface so resolver tests do not
/// touch the real filesystem; production wires up
/// <see cref="DirectoryMarkerMatcher"/>.
/// </summary>
internal interface IDirectoryMarkerMatcher
{
    /// <summary>
    /// Returns <c>true</c> when at least one of <paramref name="markers"/>
    /// matches a file under <paramref name="directory"/>. An empty
    /// <paramref name="markers"/> list returns <c>true</c> (empty markers
    /// mean "always candidate", per spec §5.2 pre-filter rule).
    /// </summary>
    public bool AnyMatch(DirectoryInfo directory, IReadOnlyList<string> markers);
}
