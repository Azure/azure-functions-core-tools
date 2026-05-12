// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Immutable point-in-time view of <see cref="DashboardState"/>. Renderers
/// consume snapshots; they never mutate.
/// </summary>
internal sealed record DashboardSnapshot(
    HostLifecycleState HostState,
    string? HostVersion,
    string? ListenUri,
    DateTimeOffset StartedAt,
    IReadOnlyList<FunctionInfo> Functions,
    int ActiveInvocationCount,
    int TotalInvocations,
    int SucceededInvocations,
    int FailedInvocations,
    int ErrorCount);
