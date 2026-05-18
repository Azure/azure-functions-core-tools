// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Outcome of <see cref="IProjectResolver.EvaluateAsync"/>. <see cref="Reason"/>
/// shows up verbatim in disambiguation messages.
/// </summary>
public sealed record EvaluationResult(bool IsMatch, string? Reason = null, string? WorkerRuntime = null)
{
    public static EvaluationResult NoMatch(string? reason = null) => new(false, reason);

    public static EvaluationResult Match(string? reason = null, string? workerRuntime = null) =>
        new(true, reason, workerRuntime);
}
