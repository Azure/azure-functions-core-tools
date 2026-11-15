// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;

namespace Azure.Functions.Cli.Console.Clipboard;

internal sealed class WindowsClipboard(IWindowsClipboardNative native) : IWindowsClipboard
{
    private readonly IWindowsClipboardNative _native = native ?? throw new ArgumentNullException(nameof(native));

    public bool IsLocalSession => _native.IsLocalSession;

    public Task<ClipboardCopyResult> CopyAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > ClipboardWriter.MaximumLocalBytes || text.Contains('\0'))
        {
            return Task.FromResult(new ClipboardCopyResult(ClipboardCopyStatus.Failed, "The selection cannot be copied losslessly."));
        }

        // Keep the clipboard and its owner window on one worker thread, including contention retries.
        return Task.Run(() => Copy(text, cancellationToken), cancellationToken);
    }

    private ClipboardCopyResult Copy(string text, CancellationToken cancellationToken)
    {
        nint window = 0;
        nint allocation = 0;
        bool opened = false;
        bool transferred = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            window = _native.CreateOwnerWindow();
            allocation = _native.Allocate(checked((text.Length + 1) * sizeof(char)));
            _native.Write(allocation, text);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_native.TryOpen(window))
                {
                    opened = true;
                    break;
                }

                _native.WaitBeforeRetry(cancellationToken);
            }

            if (!opened)
            {
                return new ClipboardCopyResult(ClipboardCopyStatus.Failed, "The local clipboard is busy. Try copying again.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _native.Empty();
            _native.SetUnicodeData(allocation);
            transferred = true;
            return new ClipboardCopyResult(ClipboardCopyStatus.Confirmed, "Copied to the local clipboard.");
        }
        catch (Win32Exception)
        {
            return new ClipboardCopyResult(ClipboardCopyStatus.Failed, "Windows rejected the clipboard write. The selection is retained.");
        }
        finally
        {
            try
            {
                if (opened)
                {
                    _native.Close();
                }
            }
            finally
            {
                try
                {
                    if (allocation != 0 && !transferred)
                    {
                        _native.Free(allocation);
                    }
                }
                finally
                {
                    if (window != 0)
                    {
                        _native.DestroyOwnerWindow(window);
                    }
                }
            }
        }
    }
}
