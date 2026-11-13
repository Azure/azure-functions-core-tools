// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console.Terminal;

internal sealed class UnixTerminalDriver(IUnixTerminalNative native, bool isMacOS)
    : ITerminalDriver, ITerminalSuspension, ITerminalEmergencyRestoration
{
    private readonly IUnixTerminalNative _native = native ?? throw new ArgumentNullException(nameof(native));
    private readonly bool _isMacOS = isMacOS;
    private readonly object _nativeGate = new();
    private readonly VtInputDecoder _decoder = new();
    private readonly byte[] _buffer = new byte[1024];
    private byte[]? _original;
    private byte[]? _inputMode;
    private TerminalSize _lastSize;
    private bool _suspended;
    private bool _inputRestored;

    public bool IsSupported => _native.IsAvailable;

    public TerminalSize Size => _native.GetSize();

    public void Activate()
    {
        lock (_nativeGate)
        {
            if (_original is not null)
            {
                throw new InvalidOperationException("Native terminal modes have already been acquired.");
            }

            _original = _native.GetAttributes();
            _inputMode = UnixTerminalMode.CreateInputMode(_original, _isMacOS);
            _lastSize = _native.GetSize();
            _native.SetAttributes(_inputMode);
            _inputRestored = false;
        }
    }

    public IEnumerable<TerminalInputEvent> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_nativeGate)
        {
            if (_suspended)
            {
                return [];
            }

            short ready = _native.Poll(50);
            cancellationToken.ThrowIfCancellationRequested();
            if ((ready & (8 | 32)) != 0)
            {
                throw new IOException("The terminal input descriptor is no longer available.");
            }

            List<TerminalInputEvent> events = [];
            TerminalSize size = _native.GetSize();
            if (size != _lastSize)
            {
                _lastSize = size;
                events.Add(new TerminalInputEvent(TerminalInputKind.Resize));
            }

            if ((ready & (1 | 16)) != 0)
            {
                int count = _native.Read(_buffer);
                if (count == 0 && (ready & 16) != 0)
                {
                    throw new EndOfStreamException("The terminal input stream closed.");
                }

                if (count == 0)
                {
                    // VMIN=0 can race a signal's input-queue flush; do not spin on an empty ready read.
                    cancellationToken.WaitHandle.WaitOne(10);
                    cancellationToken.ThrowIfCancellationRequested();
                    events.AddRange(_decoder.ExpireEscape());
                }
                else
                {
                    events.AddRange(_decoder.Feed(_buffer.AsSpan(0, count)));
                }
            }
            else
            {
                events.AddRange(_decoder.ExpireEscape());
            }

            return events;
        }
    }

    public void SetMouseCapture(bool enabled)
    {
        // Unix capture is a terminal protocol mode, serialized by TerminalSession.
    }

    public IDisposable ObserveSuspension(Action suspend, Action resume)
    {
        ArgumentNullException.ThrowIfNull(suspend);
        ArgumentNullException.ThrowIfNull(resume);
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("POSIX terminal suspension is not available on Windows.");
        }

        return new UnixTerminalSignals(suspend, resume);
    }

    public void Suspend()
    {
        lock (_nativeGate)
        {
            if (_original is not null)
            {
                _suspended = true;
                _native.SetAttributes(_original);
                _inputRestored = true;
                // PosixSignalRegistration does not perform SIGTSTP's default stop action.
                _native.SuspendProcess();
            }
        }
    }

    public void Resume()
    {
        lock (_nativeGate)
        {
            if (_original is not null && _inputMode is not null)
            {
                _native.SetAttributes(_inputMode);
                _suspended = false;
                _inputRestored = false;
            }
        }
    }

    public void Restore()
    {
        lock (_nativeGate)
        {
            if (_original is not null)
            {
                byte[] original = _original;
                _original = null;
                _suspended = true;
                if (!_inputRestored)
                {
                    _native.SetAttributes(original);
                }
            }
        }
    }

    public void RestoreInput()
    {
        lock (_nativeGate)
        {
            if (_original is not null && !_inputRestored)
            {
                _native.SetAttributes(_original);
                _inputRestored = true;
                _suspended = true;
            }
        }
    }

    public void Dispose() => Restore();
}
