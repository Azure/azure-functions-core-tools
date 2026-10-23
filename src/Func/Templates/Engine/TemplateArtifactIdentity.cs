// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Identifies current managed archive bytes without treating package metadata as provenance.
/// </summary>
internal sealed record TemplateArtifactIdentity(string PackageId, string Version, string Sha256);

internal sealed record InstalledTemplateArtifact(TemplateCatalogEntry Entry,
    Acquisition.NativeTemplateSelection? Selection, string? Diagnostic)
{
    public Acquisition.BundledBasicSelection? BundledSelection { get; init; }

    public Exception? Failure { get; init; }
}
