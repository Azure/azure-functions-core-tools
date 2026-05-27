// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Downloads a template from its source repository and writes the files
/// into the target directory. Handles git clone, HTTP zip download,
/// path traversal prevention, and metadata cleanup.
/// </summary>
public interface IQuickstartScaffolder
{
    /// <summary>
    /// Scaffolds the given template entry into <paramref name="targetDirectory"/>.
    /// </summary>
    /// <param name="entry">The manifest entry describing the template to scaffold.</param>
    /// <param name="targetDirectory">Absolute path to the directory where template files are written.</param>
    /// <param name="fetchMode">Strategy for downloading the template content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ScaffoldAsync(
        QuickstartEntry entry,
        string targetDirectory,
        FetchMode fetchMode,
        CancellationToken cancellationToken = default);
}
