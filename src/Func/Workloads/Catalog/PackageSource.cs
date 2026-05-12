// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// A configured workload feed: either a v3 NuGet service-index URL or a
/// local directory containing <c>.nupkg</c> files.
/// </summary>
/// <param name="Name">Human-readable identifier surfaced in errors and telemetry.</param>
/// <param name="Location">
/// For remote sources, the absolute <c>index.json</c> URI. For local sources,
/// the absolute directory path expressed as a <c>file://</c> URI.
/// </param>
/// <param name="IsLocal">
/// <c>true</c> when the source is an on-disk directory; <c>false</c> for a
/// remote v3 feed.
/// </param>
internal sealed record PackageSource(string Name, Uri Location, bool IsLocal);
