// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Terminal;

namespace Azure.Functions.Cli.Console.Clipboard;

internal enum ClipboardCopyStatus
{
    Confirmed,
    Sent,
    Failed,
}

internal sealed record ClipboardCopyResult(ClipboardCopyStatus Status, string Message);

internal interface IClipboardWriter
{
    public Task<ClipboardCopyResult> CopyAsync(string text, ITerminalSession terminal, CancellationToken cancellationToken);
}

internal interface IWindowsClipboard
{
    public bool IsLocalSession { get; }

    public Task<ClipboardCopyResult> CopyAsync(string text, CancellationToken cancellationToken);
}
