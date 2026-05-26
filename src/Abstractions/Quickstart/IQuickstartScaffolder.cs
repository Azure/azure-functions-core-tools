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
    public Task ScaffoldAsync(
        QuickstartEntry entry,
        string targetDirectory,
        FetchMode fetchMode,
        CancellationToken cancellationToken = default);
}
