// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// A service contribution paired with the workload that registered it. Resolved
/// from DI when the host needs to know who supplied a given service — for
/// example, to compute the alias column in <c>func workload list</c> or to
/// prefix workload-specific errors with the source package id.
/// </summary>
/// <typeparam name="T">The contributed service type (e.g. <see cref="IProjectInitializer"/>).</typeparam>
public sealed record WorkloadContribution<T>(IWorkload Owner, T Service)
    where T : class;
