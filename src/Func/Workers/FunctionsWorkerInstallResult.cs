// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Result of installing a Functions worker workload.
/// </summary>
/// <param name="Worker">The worker resolved from the installed content workload.</param>
/// <param name="WorkloadInstallResult">The generic workload installation result.</param>
internal sealed record FunctionsWorkerInstallResult(IFunctionsWorker Worker, WorkloadInstallResult WorkloadInstallResult);
