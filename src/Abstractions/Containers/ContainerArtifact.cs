// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Containers;

/// <summary>
/// Describes one text artifact generated relative to a Functions project root.
/// </summary>
public sealed record ContainerArtifact
{
    public ContainerArtifact(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        RelativePath = relativePath;
        Content = content;
    }

    /// <summary>
    /// Gets the output path relative to the project root.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets the complete text content to write.
    /// </summary>
    public string Content { get; }
}
