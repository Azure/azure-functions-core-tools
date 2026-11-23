// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Identifies a workload template root and its owning package.
/// </summary>
/// <param name="Path">The path of the template root.</param>
/// <param name="PackageId">The identifier of the owning package.</param>
/// <param name="Version">The version of the owning package.</param>
/// <param name="LastChangeTime">The last change time of the template root.</param>
internal sealed record WorkloadTemplateRoot(string Path, string PackageId, string Version, DateTime LastChangeTime);
