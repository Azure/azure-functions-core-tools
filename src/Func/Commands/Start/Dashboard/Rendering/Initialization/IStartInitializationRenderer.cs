// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

/// <summary>
/// Renders startup initialization events before the dashboard event stream starts.
/// </summary>
internal interface IStartInitializationRenderer : IAsyncDisposable
{
    public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken);

    public Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken);
}
