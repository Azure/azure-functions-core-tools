// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// A single template entry from the CDN manifest.
/// </summary>
/// <param name="Id">Unique template identifier (e.g. "http-trigger-python-azd").</param>
/// <param name="DisplayName">Human-friendly name shown in prompts and list output.</param>
/// <param name="Language">Manifest language value (e.g. "Python", "CSharp", "TypeScript").</param>
/// <param name="Resource">Trigger/binding resource type (e.g. "http", "blob", "timer").</param>
/// <param name="Iac">Infrastructure-as-code type (e.g. "bicep", "terraform", "none").</param>
/// <param name="RepositoryUrl">GitHub repository URL (HTTPS only, trusted org).</param>
/// <param name="FolderPath">Subfolder within the repo containing the template, or "." for root.</param>
/// <param name="GitRef">Immutable tag or SHA pinning the template version.</param>
/// <param name="ShortDescription">One-line summary for list output.</param>
/// <param name="LongDescription">Detailed description for info output.</param>
/// <param name="WhatsIncluded">Bulleted list of what the template contains.</param>
/// <param name="Tags">Searchable tags for filtering.</param>
/// <param name="Priority">Sort priority (lower = higher in list).</param>
public sealed record QuickstartEntry(
    string Id,
    string DisplayName,
    string Language,
    string Resource,
    string? Iac,
    string RepositoryUrl,
    string FolderPath,
    string? GitRef,
    string? ShortDescription,
    string? LongDescription,
    IReadOnlyList<string>? WhatsIncluded,
    IReadOnlyList<string>? Tags,
    int Priority);
