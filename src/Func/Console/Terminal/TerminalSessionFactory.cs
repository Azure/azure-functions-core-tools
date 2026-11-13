// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Spectre.Console;

namespace Azure.Functions.Cli.Console.Terminal;

internal interface ITerminalInputHandoff
{
    public bool CanHandoffManagedInput { get; }

    public IEnumerable<TerminalInputEvent> DrainManagedInput(IAnsiConsole console, CancellationToken cancellationToken);
}

internal sealed class TerminalSessionFactory(IPlatform platform) : ITerminalSessionFactory, ITerminalInputHandoff
{
    private readonly IPlatform _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    private readonly ITerminalDriverFactory _drivers = new NativeTerminalDriverFactory(platform);
    private WindowsInputNormalizer _managedInput = new();

    internal TerminalSessionFactory(IPlatform platform, ITerminalDriverFactory drivers)
        : this(platform)
    {
        _drivers = drivers ?? throw new ArgumentNullException(nameof(drivers));
    }

    // Unix's managed reader can retain a partial UTF-8 code point outside its public key buffer.
    // A caller that has used managed input must choose plain output there, not discard hidden bytes.
    public bool CanHandoffManagedInput => _platform.IsWindows;

    public bool IsSupported(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (!console.Profile.Capabilities.Ansi || !console.Profile.Capabilities.Interactive
            || !console.Profile.Capabilities.AlternateBuffer || !console.Profile.Out.IsTerminal)
        {
            return false;
        }

        try
        {
            using ITerminalDriver driver = _drivers.Create();
            return driver.IsSupported && driver.Size is { Width: > 0, Height: > 0 };
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or PlatformNotSupportedException
            or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public ITerminalSession Open(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (!IsSupported(console))
        {
            throw new PlatformNotSupportedException("Full-screen input is unavailable for this terminal. Use --no-tui.");
        }

        TerminalSession session = new(console, _drivers.Create(_managedInput));
        session.Activate();
        _managedInput = new();
        return session;
    }

    /// <summary>
    /// Transfers pending managed keys after all prompt reads have joined and before Open is called.
    /// Callers must consume these events, not flush them; do not call this once native input owns the terminal.
    /// An incomplete UTF-16 pair is retained for the next drain or transferred to Open on this same factory.
    /// </summary>
    public IEnumerable<TerminalInputEvent> DrainManagedInput(IAnsiConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (!CanHandoffManagedInput)
        {
            throw new PlatformNotSupportedException(
                "Lossless transfer from Unix managed prompt input is unavailable. Use plain output after an interactive prompt.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!console.Input.IsKeyAvailable())
            {
                yield break;
            }

            ConsoleKeyInfo? key = console.Input.ReadKey(intercept: true);
            if (!key.HasValue)
            {
                yield break;
            }

            foreach (TerminalInputEvent input in _managedInput.DecodeManagedKey(key.Value))
            {
                yield return input;
            }
        }
    }
}

internal sealed class NativeTerminalDriverFactory(IPlatform platform) : ITerminalDriverFactory
{
    private readonly IPlatform _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public ITerminalDriver Create(WindowsInputNormalizer? managedInput = null)
    {
        return _platform.IsWindows
            ? new WindowsTerminalDriver(new WindowsTerminalNative(), managedInput)
            : new UnixTerminalDriver(new UnixTerminalNative(_platform.IsMacOS), _platform.IsMacOS);
    }
}
