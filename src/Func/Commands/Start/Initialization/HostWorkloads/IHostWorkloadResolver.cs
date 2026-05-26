// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves the Azure Functions host workload used by start initialization.
/// </summary>
internal interface IHostWorkloadResolver
{
    public Task<HostWorkloadResolution> ResolveAsync(HostWorkloadResolutionContext context, CancellationToken cancellationToken);
}
