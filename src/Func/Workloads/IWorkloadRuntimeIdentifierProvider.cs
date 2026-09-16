// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

internal interface IWorkloadRuntimeIdentifierProvider
{
    public string Current { get; }
}

internal sealed class WorkloadRuntimeIdentifierProvider : IWorkloadRuntimeIdentifierProvider
{
    public string Current => WorkloadRuntimeIdentifier.Current;
}
