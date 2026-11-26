// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Metadata for one version of a package on one source.
/// </summary>
/// <param name="PackageId">Package id as published on the source.</param>
/// <param name="LatestVersion">Version described by this row.</param>
/// <param name="Title">Optional <c>title</c> from the package metadata.</param>
/// <param name="Description">Optional <c>description</c> from the package metadata.</param>
/// <param name="Aliases">CLI-facing aliases parsed from <c>alias:&lt;name&gt;</c> tags.</param>
/// <param name="Source">Source the result was discovered on; useful for follow-up download.</param>
internal sealed record CatalogSearchResult(
    string PackageId,
    NuGetVersion LatestVersion,
    string? Title,
    string? Description,
    IReadOnlyList<string> Aliases,
    PackageSource Source)
{
    public StackDescriptor? Stack { get; init; }

    /// <summary>
    /// Lowercased value parsed from the <c>kind:</c> NuGet tag (e.g. <c>workload</c>,
    /// <c>content</c>, <c>meta</c>). <see langword="null"/> when the package omits the
    /// tag; this describes the package shape, not whether it provides a project stack.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Lowercased value parsed from the <c>rid:&lt;rid&gt;</c> NuGet tag, set on
    /// per-RID workload packages (host, python worker). <see langword="null"/>
    /// for single-RID packages. Lets the alias resolver pick the variant that
    /// matches the current platform when an alias spans multiple per-RID packs.
    /// </summary>
    public string? Rid { get; init; }

    /// <summary>
    /// Release channel this row represents (e.g. <c>stable</c>, <c>preview</c>,
    /// <c>experimental</c>). Set when <c>func workload search</c> expands a
    /// single package into one row per channel so users can install a
    /// non-latest channel. <see langword="null"/> when channel grouping was
    /// not requested.
    /// </summary>
    public string? Channel { get; init; }
}

/// <summary>
/// A package id + version pinned to the source it was discovered on. Returned
/// by the resolve methods so the caller can download without re-resolving.
/// </summary>
internal sealed record ResolvedPackage(string PackageId, NuGetVersion Version, PackageSource Source);
