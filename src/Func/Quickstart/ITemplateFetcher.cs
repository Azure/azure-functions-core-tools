// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches template content from a remote source into a local temp directory.
/// Each implementation handles one <see cref="FetchMode"/> strategy.
/// </summary>
internal interface ITemplateFetcher
{
    /// <summary>
    /// The fetch mode this implementation handles.
    /// </summary>
    public FetchMode Mode { get; }

    /// <summary>
    /// Downloads/clones the template into <paramref name="tempDirectory"/>.
    /// </summary>
    public Task FetchAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken);
}
