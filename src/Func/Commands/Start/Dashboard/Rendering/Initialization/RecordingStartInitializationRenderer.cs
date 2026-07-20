// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

/// <summary>
/// Records startup initialization events while forwarding them to the selected renderer.
/// </summary>
internal sealed class RecordingStartInitializationRenderer(IStartInitializationRenderer inner) : IStartInitializationRenderer
{
    private readonly IStartInitializationRenderer _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly List<StartInitializationEvent> _events = [];

    public IReadOnlyList<StartInitializationEvent> Events => _events;

    public bool HasCompleted { get; private set; }

    public async Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initializationEvent);

        _events.Add(initializationEvent);
        if (initializationEvent is StartInitializationCompletedEvent)
        {
            HasCompleted = true;
        }

        await _inner.OnEventAsync(initializationEvent, cancellationToken);
    }

    public async Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
        => await _inner.ConfirmAsync(prompt, defaultValue, cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
