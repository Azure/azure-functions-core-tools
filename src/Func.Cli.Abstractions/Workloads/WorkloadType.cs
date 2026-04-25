// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Categorization for a workload, surfaced in <c>func workload list</c> and
/// available for filtering/grouping. Set by the workload author on its
/// <see cref="IWorkload"/> implementation.
/// </summary>
public enum WorkloadType
{
    /// <summary>
    /// A language / runtime stack (e.g. dotnet, node, python). Typically
    /// contributes <see cref="IProjectInitializer"/> and templates.
    /// </summary>
    Stack,

    /// <summary>A standalone tool or developer utility shipped as a workload.</summary>
    Tool,

    /// <summary>An extension that augments existing CLI commands without owning a stack.</summary>
    Extension,
}
