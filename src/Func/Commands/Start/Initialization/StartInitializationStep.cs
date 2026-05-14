// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Metadata for a startup initialization step.
/// </summary>
internal sealed record StartInitializationStep(
    StartInitializationStepKind Kind,
    string Title,
    string? Detail = null,
    StartInitializationDisplayKind DisplayKind = StartInitializationDisplayKind.Status);
