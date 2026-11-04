// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Console.Terminal;

internal sealed class TerminalSession(IAnsiConsole console, ITerminalDriver driver) : ITerminalSession
{
    internal const string EnableMouse = "\u001b[?1000l\u001b[?1003l\u001b[?1002h\u001b[?1006h";
    internal const string DisableMouse = "\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l";
    private const string ReenterScreen = "\u001b[?1049h\u001b[?25l\u001b[?7l\u001b[?1004h\u001b[?2004h" + EnableMouse;

    // DECSAVE/DECRESTORE preserve private modes on terminals that implement them.
    // Others receive explicit disable/show defaults during teardown.
    internal const string EnterScreen =
        "\u001b[?25s\u001b[?7s\u001b[?1000s\u001b[?1002s\u001b[?1003s\u001b[?1004s\u001b[?1006s\u001b[?2004s"
        + ReenterScreen;

    internal const string LeaveScreen =
        DisableMouse + "\u001b[?1004l\u001b[?2004l\u001b[0m\u001b[?7h\u001b[?25h\u001b[?1049l"
        + "\u001b[?7r\u001b[?25r\u001b[?1000r\u001b[?1002r\u001b[?1003r\u001b[?1004r\u001b[?1006r\u001b[?2004r";

    private readonly IAnsiConsole _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly ITerminalDriver _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    private readonly object _outputGate = new();
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ManualResetEventSlim _restorationCompleted = new();
    private readonly TerminalInputQueue _input = new();
    private Task _reader = Task.CompletedTask;
    private Task? _disposal;
    private Task? _restoration;
    private IDisposable? _suspension;
    private ExceptionDispatchInfo? _signalFailure;
    private ExceptionDispatchInfo? _restorationFailure;
    private bool _protocolTouched;
    private bool _activated;
    private bool _restored;
    private bool _stopDisposed;
    private bool _suspended;
    private bool _mouseCapture = true;
    private int _consumer;
    private int _resumePending;

    public TerminalSize Size => _driver.Size;

    internal void Activate()
    {
        try
        {
            lock (_outputGate)
            {
                ObjectDisposedException.ThrowIf(_restored, this);
                if (_activated)
                {
                    throw new InvalidOperationException("The terminal session is already active.");
                }

                _driver.Activate();
                _protocolTouched = true;
                WriteCore(EnterScreen);
                _activated = true;
                if (_driver is ITerminalSuspension suspension)
                {
                    _suspension = suspension.ObserveSuspension(Suspend, Resume);
                }

                _reader = Task.Run(ReadLoopAsync);
            }
        }
        catch
        {
            try
            {
                Restore();
            }
            catch
            {
                // Acquisition must retain its original failure even if emergency restoration also fails.
            }

            _suspension?.Dispose();
            if (_restoration?.IsCompleted == true)
            {
                _driver.Dispose();
                _stop.Dispose();
                _stopDisposed = true;
                _restorationCompleted.Dispose();
            }

            throw;
        }
    }

    public async IAsyncEnumerable<TerminalInputEvent> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _consumer, 1) != 0)
        {
            throw new InvalidOperationException("A terminal session supports only one input consumer.");
        }

        using var inputStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        try
        {
            await foreach (TerminalInputEvent input in _input.ReadAsync(inputStop.Token))
            {
                yield return input;
            }
        }
        finally
        {
            lock (_lifetimeGate)
            {
                if (!_stopDisposed)
                {
                    _stop.Cancel();
                }
            }

            await _reader;
        }
    }

    public void Render(IRenderable frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_outputGate)
        {
            ThrowIfInactive();
            if (_suspended)
            {
                return;
            }

            _console.Profile.Out.Writer.Write("\u001b[H");
            _console.Write(frame);
            _console.Profile.Out.Writer.Flush();
        }
    }

    public void WriteControl(string sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        lock (_outputGate)
        {
            ThrowIfInactive();
            if (_suspended)
            {
                throw new InvalidOperationException("The terminal session is suspended.");
            }

            WriteCore(sequence);
        }
    }

    public void SetMouseCapture(bool enabled)
    {
        lock (_outputGate)
        {
            ThrowIfInactive();
            _mouseCapture = enabled;
            if (_suspended)
            {
                return;
            }

            _driver.SetMouseCapture(enabled);
            WriteCore(enabled ? EnableMouse : DisableMouse);
        }
    }

    public void Restore()
    {
        Task restoration = RequestRestoration(out bool started);
        if (!started)
        {
            return;
        }

        // Force-exit callers must never wait for a blocked frame or a stalled terminal transport.
        // A dedicated cleanup thread restores input first, even if a writer owns the output gate.
        lock (_lifetimeGate)
        {
            if (!restoration.IsCompleted)
            {
                _restorationCompleted.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(150));
            }
        }

        _restorationFailure?.Throw();
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            _disposal ??= DisposeCoreAsync();
            return new ValueTask(_disposal);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (Volatile.Read(ref _suspended))
                {
                    await Task.Delay(50, _stop.Token);
                    continue;
                }

                if (Interlocked.Exchange(ref _resumePending, 0) != 0)
                {
                    await _input.WriteAsync(new TerminalInputEvent(TerminalInputKind.FocusLost), _stop.Token);
                    await _input.WriteAsync(new TerminalInputEvent(TerminalInputKind.Resize), _stop.Token);
                }

                foreach (TerminalInputEvent input in _driver.Read(_stop.Token))
                {
                    await _input.WriteAsync(input, _stop.Token);
                }
            }

            _signalFailure?.Throw();
            _input.Complete();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            _signalFailure?.Throw();
            _input.Complete();
        }
        catch (Exception exception)
        {
            // Relay every native-reader failure to the supervised consumer, including while the host is idle.
            _input.Complete(exception);
            throw;
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task restoration = RequestRestoration(out _);
        ExceptionDispatchInfo? failure = null;
        try
        {
            await _reader;
        }
        catch (Exception exception)
        {
            // Retain the reader's original failure while still completing every cleanup stage.
            failure = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            _suspension?.Dispose();
            await restoration.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            _restorationFailure?.Throw();
        }
        catch (Exception exception)
        {
            // A cleanup error must not replace the input failure that initiated shutdown.
            failure ??= ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            if (restoration.IsCompleted)
            {
                try
                {
                    _driver.Dispose();
                }
                catch (Exception exception)
                {
                    // Finish releasing managed lifetime resources even when native disposal reports a failure.
                    failure ??= ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    lock (_lifetimeGate)
                    {
                        _stop.Dispose();
                        _stopDisposed = true;
                        _restorationCompleted.Dispose();
                    }
                }
            }
        }

        failure?.Throw();
    }

    private Task RequestRestoration(out bool started)
    {
        lock (_lifetimeGate)
        {
            started = _restoration is null;
            if (_restoration is null)
            {
                Volatile.Write(ref _restored, true);
                StopInput();
                _restoration = Task.Factory.StartNew(
                    RestoreCore, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            return _restoration;
        }
    }

    private void RestoreCore()
    {
        try
        {
            try
            {
                if (_driver is ITerminalEmergencyRestoration emergency)
                {
                    emergency.RestoreInput();
                }
            }
            catch (Exception exception)
            {
                // Input failure cannot prevent an independent attempt to restore the visible screen.
                _restorationFailure = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                if (System.Threading.Monitor.TryEnter(_outputGate))
                {
                    try
                    {
                        if (_protocolTouched)
                        {
                            WriteCore(LeaveScreen);
                            _protocolTouched = false;
                        }
                    }
                    finally
                    {
                        System.Threading.Monitor.Exit(_outputGate);
                    }
                }
            }
            catch (Exception exception)
            {
                // Native flags still need restoring if writing a disable sequence fails.
                _restorationFailure ??= ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                _driver.Restore();
            }
            catch (Exception exception)
            {
                // Cleanup errors are delivered to Restore/Dispose, never thrown on the emergency worker thread.
                _restorationFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }
        finally
        {
            _restorationCompleted.Set();
        }
    }

    private void Suspend()
    {
        lock (_outputGate)
        {
            if (_restored || _suspended)
            {
                return;
            }

            try
            {
                _suspended = true;
                try
                {
                    WriteCore(LeaveScreen);
                    _protocolTouched = false;
                }
                finally
                {
                    ((ITerminalSuspension)_driver).Suspend();
                }
            }
            catch (Exception exception)
            {
                // Signal callbacks cannot throw into the runtime; the input consumer supervises the failure instead.
                _signalFailure = ExceptionDispatchInfo.Capture(exception);
                _input.Complete(exception);
                StopInput();
            }
        }
    }

    private void Resume()
    {
        lock (_outputGate)
        {
            if (_restored)
            {
                return;
            }

            try
            {
                ((ITerminalSuspension)_driver).Resume();
                bool saveModes = !_protocolTouched;
                _protocolTouched = true;
                WriteCore(saveModes ? EnterScreen : ReenterScreen);
                if (!_mouseCapture)
                {
                    _driver.SetMouseCapture(false);
                    WriteCore(DisableMouse);
                }

                _suspended = false;
                Interlocked.Exchange(ref _resumePending, 1);
            }
            catch (Exception exception)
            {
                // Resume failures are lifecycle failures, not unobserved exceptions on a signal callback thread.
                _signalFailure = ExceptionDispatchInfo.Capture(exception);
                _input.Complete(exception);
                StopInput();
            }
        }
    }

    private void ThrowIfInactive()
    {
        ObjectDisposedException.ThrowIf(_restored, this);
        if (!_activated)
        {
            throw new InvalidOperationException("The terminal session has not been activated.");
        }
    }

    private void StopInput()
    {
        lock (_lifetimeGate)
        {
            if (!_stopDisposed)
            {
                _stop.Cancel();
            }
        }
    }

    private void WriteCore(string sequence)
    {
        _console.Profile.Out.Writer.Write(sequence);
        _console.Profile.Out.Writer.Flush();
    }
}
