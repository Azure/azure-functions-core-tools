// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Identifies a startup initialization step.
/// </summary>
internal enum StartInitializationStepKind
{
    ResolveProfile,
    ResolveConstraints,
    ResolveHostWorkload,
    InstallHostWorkload,
    ResolveStack,
    ResolveBundle,
    InstallBundle,
    StartHost,
}
