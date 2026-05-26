// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Fully inherited and validated profile constraints.
/// </summary>
internal sealed record ResolvedProfile(
    string Name,
    ProfileSourceInfo Source,
    string? Sku,
    ProfileStatus Status,
    string? DeprecationUrl,
    VersionRange HostVersionRange,
    IReadOnlyDictionary<string, VersionRange> WorkerVersionRanges,
    VersionRange? ExtensionBundleVersionRange,
    IReadOnlyList<string>? SupportedRuntimes,
    string? Notes);
