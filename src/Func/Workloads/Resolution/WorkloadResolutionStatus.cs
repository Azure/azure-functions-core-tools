// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Status of a <see cref="WorkloadResolution"/>. Drives whether callers
/// dispatch to the chosen workload or surface an error.
/// </summary>
internal enum WorkloadResolutionStatus
{
    /// <summary>Exactly one workload owns the directory.</summary>
    Resolved,

    /// <summary>More than one workload claims the directory; user must disambiguate.</summary>
    Ambiguous,

    /// <summary>No installed workload claims the directory.</summary>
    None,
}
