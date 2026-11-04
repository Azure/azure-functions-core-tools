// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Azure.Functions.Cli.Console.Terminal;

internal interface ITerminalSuspension
{
    public IDisposable ObserveSuspension(Action suspend, Action resume);

    public void Suspend();

    public void Resume();
}

[UnsupportedOSPlatform("windows")]
internal sealed class UnixTerminalSignals : IDisposable
{
    private readonly PosixSignalRegistration _suspend;
    private readonly PosixSignalRegistration _resume;

    public UnixTerminalSignals(Action suspend, Action resume)
    {
        ArgumentNullException.ThrowIfNull(suspend);
        ArgumentNullException.ThrowIfNull(resume);
        _suspend = PosixSignalRegistration.Create(PosixSignal.SIGTSTP, context =>
        {
            context.Cancel = true;
            suspend();
        });
        try
        {
            _resume = PosixSignalRegistration.Create(PosixSignal.SIGCONT, context =>
            {
                // Prevent the runtime from overwriting the terminal modes reapplied by resume.
                context.Cancel = true;
                resume();
            });
        }
        catch
        {
            _suspend.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _suspend.Dispose();
        _resume.Dispose();
    }
}
