// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Reserves the CDN-backed template search boundary independently of NuGet package acquisition.
/// </summary>
internal class TemplateSearch
{
    /// <summary>
    /// Reserves search against the published template index; the manifest and result contracts are not yet implemented.
    /// </summary>
    /// <exception cref="NotSupportedException">CDN-backed template search is not yet available.</exception>
    public virtual Task SearchAsync(string? query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new NotSupportedException(
            "CDN-backed template search is not yet available."));
    }
}
