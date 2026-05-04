// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// A workload that has been hydrated from the global manifest and is
/// ready to participate in DI. Pairs the runtime <see cref="IWorkload"/>
/// instance with its manifest-sourced display info.
/// </summary>
/// <param name="Instance">The workload's runtime instance.</param>
/// <param name="Info">CLI-side metadata sourced from the global manifest.</param>
internal sealed record LoadedWorkload(IWorkload Instance, WorkloadInfo Info);
