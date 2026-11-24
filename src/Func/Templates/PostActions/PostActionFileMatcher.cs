// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Utils;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Matches source-file patterns against an independent snapshot of template effects.
/// </summary>
internal sealed class PostActionFileMatcher(IEnumerable<IFileChange2>? fileChanges) : IPostActionFileMatcher
{
    private readonly (string Source, string Target)[] _files = fileChanges is null
        ? []
        : [.. fileChanges.Select(change => (change.SourceRelativePath, change.TargetRelativePath))];

    /// <inheritdoc />
    public IReadOnlyList<string> MatchTargetPaths(string sourcePattern)
    {
        ArgumentNullException.ThrowIfNull(sourcePattern);
        var glob = Glob.Parse(sourcePattern);
        return [.. _files.Where(file => glob.IsMatch(file.Source)).Select(file => file.Target)];
    }
}
