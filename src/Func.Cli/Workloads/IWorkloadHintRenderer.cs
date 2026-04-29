// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Renders a <see cref="WorkloadHint"/> to the user. The default
/// implementation writes through <c>IInteractionService</c>; tests substitute
/// a recording renderer to assert on the hint shape directly.
/// </summary>
internal interface IWorkloadHintRenderer
{
    public void Render(WorkloadHint hint);
}
