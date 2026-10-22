// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;

// Force UTF-8 stdout so glyphs like →, ●, ✗, ◉ render correctly on Windows
// consoles (which default to OEM/ANSI code pages that map them to '?').
// Safe on non-Windows platforms: the runtime is already UTF-8 there, this is
// effectively a no-op. Some legacy terminals throw on Out/Error encoding
// changes (e.g., redirected to a pipe that doesn't allow it), so guard.
try
{
    System.Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (System.IO.IOException)
{
    // Output isn't a real console (e.g., closed handle) — proceed regardless.
}

DefaultTheme theme = new();
SpectreInteractionService interaction = new(theme);

// Wire cancellation to Ctrl+C / SIGTERM
// First Ctrl+C: graceful shutdown. Second Ctrl+C: force exit.
using var cts = new CancellationTokenSource();
int ctrlCCount = 0;
Console.CancelKeyPress += (_, e) =>
{
    ctrlCCount++;
    if (ctrlCCount == 1)
    {
        e.Cancel = true;
        cts.Cancel();
    }
    else
    {
        // Second Ctrl+C — force exit immediately
        Environment.Exit(130);
    }
};

return await new NativeCliEntryPoint().ExecuteAsync(args, interaction, new(), new(), cts.Token);
