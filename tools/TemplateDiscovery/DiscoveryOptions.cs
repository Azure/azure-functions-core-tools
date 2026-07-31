// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Parsed command-line configuration for a single index-build run.
/// </summary>
internal sealed record DiscoveryOptions(
    DirectoryInfo OutputPath,
    DirectoryInfo? PackagesPath,
    string? Feed,
    IReadOnlyList<string> PackageTypes,
    bool Diff,
    bool IncludePrerelease,
    bool NoTemplateJsonFilter,
    bool Verbose);
