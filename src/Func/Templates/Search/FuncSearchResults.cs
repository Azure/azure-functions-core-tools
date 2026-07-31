// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Outcome of a <c>func new --search</c> query: the matched packages with
/// their installed-state annotation, plus the echoed query context.
/// </summary>
/// <param name="Term">The search term, or <c>null</c> for a full listing.</param>
/// <param name="Source">The feed searched directly (<c>--source</c>), or <c>null</c> for the index.</param>
/// <param name="Packages">Matched packages, already ordered for display.</param>
internal sealed record FuncSearchResults(string? Term, string? Source, IReadOnlyList<FuncSearchPackageResult> Packages);

/// <summary>
/// A matched package with the templates that matched and its installed state.
/// </summary>
/// <param name="PackageId">NuGet package id.</param>
/// <param name="Version">Version recorded in the index / feed, when known.</param>
/// <param name="Templates">Matched templates (empty for feed results that carry only package metadata).</param>
/// <param name="Installed">Whether and how the package is already installed.</param>
internal sealed record FuncSearchPackageResult(
    string PackageId,
    string? Version,
    IReadOnlyList<FuncSearchTemplateResult> Templates,
    FuncTemplateInstalledState Installed);

/// <summary>
/// A matched template projected for display.
/// </summary>
/// <param name="Name">Template display name.</param>
/// <param name="ShortNames">Short names usable with <c>func new --template</c>.</param>
/// <param name="Stack">Stack tag (<c>azfunc-stack</c>), when present.</param>
/// <param name="Language">Language tag, when present.</param>
internal sealed record FuncSearchTemplateResult(string Name, IReadOnlyList<string> ShortNames, string? Stack, string? Language);

/// <summary>
/// Installed state of a search result, so users can tell discover-and-install
/// from already-available templates.
/// </summary>
internal abstract record FuncTemplateInstalledState
{
    private FuncTemplateInstalledState()
    {
    }

    /// <summary>
    /// The package is not installed in the func hive.
    /// </summary>
    public sealed record NotInstalled : FuncTemplateInstalledState;

    /// <summary>
    /// The package is installed and no newer version is offered by the index.
    /// </summary>
    /// <param name="Version">The installed version.</param>
    public sealed record Installed(string Version) : FuncTemplateInstalledState;

    /// <summary>
    /// The package is installed but the index offers a newer version.
    /// </summary>
    /// <param name="InstalledVersion">Currently installed version.</param>
    /// <param name="AvailableVersion">Newer version available from the index / feed.</param>
    public sealed record UpdateAvailable(string InstalledVersion, string AvailableVersion) : FuncTemplateInstalledState;
}
