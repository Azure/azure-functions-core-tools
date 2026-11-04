// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console.Terminal;

internal sealed class WindowsTerminalDriver(IWindowsTerminalNative native, WindowsInputNormalizer? managedInput = null)
    : ITerminalDriver, ITerminalEmergencyRestoration
{
    internal const uint ProcessedInput = 0x0001;
    internal const uint LineInput = 0x0002;
    internal const uint EchoInput = 0x0004;
    internal const uint WindowInput = 0x0008;
    internal const uint MouseInput = 0x0010;
    internal const uint QuickEditMode = 0x0040;
    internal const uint ExtendedFlags = 0x0080;
    internal const uint VirtualTerminalInput = 0x0200;
    internal const uint VirtualTerminalOutput = 0x0004;
    internal const uint DisableNewlineAutoReturn = 0x0008;
    private readonly IWindowsTerminalNative _native = native ?? throw new ArgumentNullException(nameof(native));
    private readonly WindowsInputNormalizer _normalizer = managedInput ?? new();
    private readonly object _modeGate = new();
    private uint _originalInput;
    private uint _originalOutput;
    private WindowsCursorInfo _originalCursor;
    private bool _saved;
    private bool _inputRestored;

    public bool IsSupported => _native.IsAvailable;

    public TerminalSize Size
    {
        get
        {
            WindowsConsoleWindow window = _native.GetWindow();
            return new TerminalSize(window.Width, window.Height);
        }
    }

    public void Activate()
    {
        lock (_modeGate)
        {
            if (_saved)
            {
                throw new InvalidOperationException("Native terminal modes have already been acquired.");
            }

            _originalInput = _native.GetInputMode();
            _originalOutput = _native.GetOutputMode();
            _originalCursor = _native.GetCursor();
            _saved = true;
            _inputRestored = false;
            _native.SetOutputMode(_originalOutput | 3 | VirtualTerminalOutput | DisableNewlineAutoReturn);
            SetMouseCapture(true);
        }
    }

    public IEnumerable<TerminalInputEvent> Read(CancellationToken cancellationToken)
    {
        if (!_native.WaitForInput(cancellationToken))
        {
            return _normalizer.ExpireEscape();
        }

        cancellationToken.ThrowIfCancellationRequested();
        WindowsInputRecord record = _native.Read();
        return _normalizer.Decode(record, _native.GetWindow());
    }

    public void SetMouseCapture(bool enabled)
    {
        lock (_modeGate)
        {
            if (!_saved)
            {
                throw new InvalidOperationException("Native terminal modes are not owned by this session.");
            }

            uint mode = (_originalInput | WindowInput | ExtendedFlags | VirtualTerminalInput)
                & ~(ProcessedInput | LineInput | EchoInput | MouseInput | QuickEditMode);
            mode |= enabled ? MouseInput : _originalInput & QuickEditMode;
            _native.SetInputMode(mode);
            _inputRestored = false;
            if (!enabled)
            {
                _normalizer.ResetPointer();
            }
        }
    }

    public void RestoreInput()
    {
        lock (_modeGate)
        {
            if (_saved && !_inputRestored)
            {
                _native.SetInputMode(_originalInput);
                _inputRestored = true;
            }
        }
    }

    public void Restore()
    {
        lock (_modeGate)
        {
            if (!_saved)
            {
                return;
            }

            _saved = false;
            try
            {
                if (!_inputRestored)
                {
                    _native.SetInputMode(_originalInput);
                }
            }
            finally
            {
                try
                {
                    _native.SetOutputMode(_originalOutput);
                }
                finally
                {
                    _native.SetCursor(_originalCursor);
                }
            }
        }
    }

    public void Dispose() => Restore();
}
