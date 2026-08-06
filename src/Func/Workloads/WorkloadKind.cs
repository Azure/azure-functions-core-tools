// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Discriminator for workload package shapes declared in
/// <c>workload.json</c>'s <c>kind</c> field.
/// </summary>
internal enum WorkloadKind
{
    /// <summary>
    /// Default. Carries a runtime payload and a <see cref="Workload"/>
    /// entry-point class; the loader activates it and calls
    /// <see cref="Workload.Configure"/>.
    /// </summary>
    Workload = 0,

    /// <summary>
    /// Carries a payload but no entry-point class. The loader skips
    /// activation; built-in commands resolve the install directory by
    /// package id (e.g. the Functions host runtime).
    /// </summary>
    Content = 1,

    /// <summary>
    /// Carries no payload of its own. Bundles other workload-or-content
    /// packages via the <c>.nuspec</c>'s <c>&lt;dependencies&gt;</c>. The
    /// loader never activates it.
    /// </summary>
    Meta = 2,

    /// <summary>
    /// Carries no payload. Maps supported runtime identifiers to exact
    /// implementation package ids for install and update resolution.
    /// </summary>
    RidPointer = 3,
}
