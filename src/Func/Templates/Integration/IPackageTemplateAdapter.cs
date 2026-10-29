// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PackageIntake;

namespace Azure.Functions.Cli.Templates.Integration;

/// <summary>
/// Adapts retained package content into exact-bound neutral candidates within a private native session.
/// </summary>
internal interface IPackageTemplateAdapter
{
    /// <summary>
    /// Validates original metadata before native normalization, preserving declaration distinctions and authored diagnostics.
    /// Reads only the retained lease and keeps native objects private; a failed open must release its native resources.
    /// </summary>
    /// <remarks>
    /// Reject meaningful duplicates, distinguish absent, empty and malformed declarations, and preserve opaque identifiers.
    /// Project only user-settable parameters and bind each descriptor to its exact retained native candidate and content.
    /// </remarks>
    /// <exception cref="InvalidTemplateMetadataException">Original or projected metadata is malformed, duplicated, or inconsistent.</exception>
    /// <exception cref="NotSupportedException">A declaration uses an unsupported native representation.</exception>
    /// <exception cref="IOException">Retained content or private native state cannot be read.</exception>
    /// <exception cref="OperationCanceledException">Opening is canceled.</exception>
    public Task<PackageTemplateSession> OpenAsync(
        TemplatePackageContentLease package, TemplateEngineContext context, CancellationToken cancellationToken);
}
