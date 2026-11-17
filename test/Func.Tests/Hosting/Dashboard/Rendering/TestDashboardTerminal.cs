// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Console.Terminal;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

internal sealed class TestDashboardTerminal : ITerminalSession
{
    private readonly IAnsiConsole _console;
    private readonly bool _readKeys;
    private readonly TaskCompletionSource _inputStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _entered;
    private bool _disposed;

    public TestDashboardTerminal(IAnsiConsole console, bool readKeys = false)
    {
        _console = console;
        _readKeys = readKeys;
        if (console.Profile.Capabilities.AlternateBuffer)
        {
            WriteControl("\u001b[?1049h\u001b[H");
            _entered = true;
        }
    }

    public TerminalSize Size => new(_console.Profile.Width, _console.Profile.Height);

    public Task Started => Task.WhenAll(_inputStarted.Task, _firstFrame.Task);

    public async IAsyncEnumerable<TerminalInputEvent> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _inputStarted.TrySetResult();
        if (!_readKeys)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            ConsoleKeyInfo? key = await _console.Input.ReadKeyAsync(true, cancellationToken);
            if (key is not null)
            {
                yield return new TerminalInputEvent(TerminalInputKind.Key, key.Value);
            }
        }
    }

    public void Render(IRenderable frame)
    {
        _console.Write(frame);
        _firstFrame.TrySetResult();
    }

    public void WriteControl(string sequence) => _console.Write(new ControlCode(sequence));
    public void SetMouseCapture(bool enabled) { }

    public void Restore()
    {
        if (_entered)
        {
            WriteControl("\u001b[?1049l");
            _entered = false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            Restore();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }
}
