// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Pairs an <see cref="IFunctionsProjectFactory"/> with the workload that
/// registered it.
/// </summary>
internal sealed record WorkloadProjectFactoryRegistration(RuntimeWorkloadInfo Workload, IFunctionsProjectFactory Factory);
