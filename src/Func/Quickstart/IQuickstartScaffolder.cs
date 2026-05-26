// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Downloads and extracts an Azure Functions app template into the target directory.
/// </summary>
internal interface IQuickstartScaffolder
{
    /// <summary>
    /// Scaffolds the template into <paramref name="targetPath"/> using the
    /// chosen <paramref name="fetchMode"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="targetPath"/> exists and is non-empty, or
    /// when <paramref name="fetchMode"/> is <see cref="FetchMode.Git"/> but
    /// the local <c>git</c> executable is not available.
    /// </exception>
    public Task ScaffoldAsync(
        QuickstartEntry entry,
        string targetPath,
        FetchMode fetchMode,
        CancellationToken cancellationToken);
}
