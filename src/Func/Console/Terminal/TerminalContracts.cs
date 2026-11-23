// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Console.Terminal;

internal readonly record struct TerminalSize(int Width, int Height);

internal enum TerminalInputKind
{
    Key,
    Text,
    Wheel,
    PointerDown,
    PointerMove,
    PointerUp,
    Resize,
    FocusLost,
    Paste,
}

internal sealed record TerminalInputEvent(
    TerminalInputKind Kind, ConsoleKeyInfo Key = default, string? Text = null, int X = 0, int Y = 0, int Delta = 0);

internal interface ITerminalSession : IAsyncDisposable
{
    public TerminalSize Size { get; }

    public IAsyncEnumerable<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken);

    public void Render(IRenderable frame);

    public void WriteControl(string sequence);

    public void SetMouseCapture(bool enabled);

    public void Restore();
}

internal interface ITerminalSessionFactory
{
    public bool IsSupported(IAnsiConsole console);

    public ITerminalSession Open(IAnsiConsole console);
}

internal interface ITerminalDriver : IDisposable
{
    public bool IsSupported { get; }

    public TerminalSize Size { get; }

    public void Activate();

    public IEnumerable<TerminalInputEvent> Read(CancellationToken cancellationToken);

    public void SetMouseCapture(bool enabled);

    public void Restore();
}

internal interface ITerminalDriverFactory
{
    public ITerminalDriver Create(WindowsInputNormalizer? managedInput = null);
}

internal interface ITerminalEmergencyRestoration
{
    public void RestoreInput();
}
