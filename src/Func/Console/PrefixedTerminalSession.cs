// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Console.Terminal;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Console;

/// <summary>
/// Delivers prompt typeahead before the native session's input without competing readers.
/// </summary>
internal sealed class PrefixedTerminalSession(ITerminalSession inner, IReadOnlyList<TerminalInputEvent> pending) : ITerminalSession
{
    private readonly ITerminalSession _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IReadOnlyList<TerminalInputEvent> _pending = pending ?? throw new ArgumentNullException(nameof(pending));

    public TerminalSize Size => _inner.Size;

    public async IAsyncEnumerable<TerminalInputEvent> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (TerminalInputEvent input in _pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return input;
        }

        await foreach (TerminalInputEvent input in _inner.ReadAsync(cancellationToken))
        {
            yield return input;
        }
    }

    public void Render(IRenderable frame) => _inner.Render(frame);
    public void WriteControl(string sequence) => _inner.WriteControl(sequence);
    public void SetMouseCapture(bool enabled) => _inner.SetMouseCapture(enabled);
    public void Restore() => _inner.Restore();
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
