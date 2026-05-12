// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Snapshot view of a single function as known to the dashboard.
/// </summary>
internal sealed record FunctionInfo(
    string Name,
    string TriggerType,
    string? Route,
    IReadOnlyList<string> HttpMethods,
    FunctionStatus Status,
    int ActiveInvocations,
    int TotalInvocations,
    int TotalErrors,
    DateTimeOffset? LastInvocationAt,
    string? LastErrorMessage);
