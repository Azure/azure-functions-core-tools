// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Matches template source files and returns their generated target paths.
/// </summary>
public interface IPostActionFileMatcher
{
    /// <summary>
    /// Finds target paths for source paths matching a template-engine glob.
    /// </summary>
    /// <param name="sourcePattern">The source-relative glob pattern.</param>
    /// <returns>Matching target paths relative to the output directory, in file-change order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourcePattern"/> is null.</exception>
    public IReadOnlyList<string> MatchTargetPaths(string sourcePattern);
}
