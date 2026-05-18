// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Result produced by an initialization step.
/// </summary>
internal sealed record StartInitializationStepResult(string? Message)
{
    public static StartInitializationStepResult Completed(string? message = null) => new(message);
}
