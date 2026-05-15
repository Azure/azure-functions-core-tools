// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Pairs an <see cref="IProjectResolver"/> with the workload that registered
/// it so the resolver can attribute results back to their owner.
/// </summary>
internal sealed record WorkloadProjectResolverContribution(WorkloadInfo Workload, IProjectResolver Resolver);
