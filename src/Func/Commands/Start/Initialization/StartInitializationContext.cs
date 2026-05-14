// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Inputs required to initialize a host run.
/// </summary>
internal sealed record StartInitializationContext(
    WorkingDirectory WorkingDirectory,
    string CliVersion,
    string ProfileName,
    string? RequestedHostVersion,
    int DemoFunctionCount,
    double DemoSpeedMultiplier,
    bool DemoAutoExit);
