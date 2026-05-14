// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Runs startup initialization before the dashboard event stream starts.
/// </summary>
internal interface IStartInitializationRunner
{
    public Task<StartInitializationResult> RunAsync(
        StartInitializationContext context,
        IStartInitializationRenderer renderer,
        CancellationToken cancellationToken);
}
