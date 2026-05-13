// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Thrown when a workload package or version cannot be located across the
/// resolved set of sources.
/// </summary>
// TODO (workload commands PR): translate this into UX-appropriate messages
// at the command layer, alias-flavoured for the common path
// (e.g. "no workload matched 'python'") and package-flavoured for --exact /
// --source paths where the user has opted into NuGet semantics.
internal sealed class WorkloadPackageNotFoundException : Exception
{
    public WorkloadPackageNotFoundException(string message)
        : base(message)
    {
    }

    public WorkloadPackageNotFoundException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
