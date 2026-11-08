// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// A template package required by an installed workload.
/// </summary>
/// <param name="PackageId">The NuGet package identity.</param>
/// <param name="Version">The required NuGet package version.</param>
/// <param name="RelativePath">The package archive path relative to the workload content root.</param>
public sealed record WorkloadTemplatePackage(string PackageId, string Version, string RelativePath);
