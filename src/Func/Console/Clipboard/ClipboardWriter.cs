// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Console.Terminal;

namespace Azure.Functions.Cli.Console.Clipboard;

internal sealed class ClipboardWriter(
    IPlatform platform, IProcessEnvironment environment, IProcessRunner processRunner, IWindowsClipboard? windowsClipboard = null)
    : IClipboardWriter
{
    internal const int MaximumLocalBytes = 1_048_576;
    internal const int MaximumOsc52Bytes = 65_536;
    private readonly IPlatform _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    private readonly IProcessEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IWindowsClipboard _windows = windowsClipboard ?? new WindowsClipboard(new WindowsClipboardNative());
    private readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private int _copying;

    public async Task<ClipboardCopyResult> CopyAsync(string text, ITerminalSession terminal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(terminal);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _copying, 1, 0) != 0)
        {
            return new ClipboardCopyResult(ClipboardCopyStatus.Failed, "A clipboard copy is already in progress.");
        }

        try
        {
            if (text.Contains('\0'))
            {
                return new ClipboardCopyResult(ClipboardCopyStatus.Failed, "The selection contains a NUL character and cannot be copied losslessly.");
            }

            int bytes = _utf8.GetByteCount(text);
            if (bytes > MaximumLocalBytes)
            {
                return TooLarge(MaximumLocalBytes);
            }

            if (!IsRemote())
            {
                if (_platform.IsWindows && _windows.IsLocalSession)
                {
                    return await _windows.CopyAsync(text, cancellationToken);
                }

                if (_platform.IsMacOS && IsLocalMacTerminal())
                {
                    return await RunHelperAsync("/usr/bin/pbcopy", [], text, "en_US.UTF-8", cancellationToken);
                }

                if (!_platform.IsWindows && !_platform.IsMacOS)
                {
                    if (EqualsVariable("XDG_SESSION_TYPE", "wayland") && HasVariable("WAYLAND_DISPLAY")
                        && HasVariable("XDG_RUNTIME_DIR"))
                    {
                        return await RunHelperAsync("wl-copy", ["--type", "text/plain;charset=utf-8"], text, "C.UTF-8", cancellationToken);
                    }

                    string? display = _environment.Get("DISPLAY");
                    if (EqualsVariable("XDG_SESSION_TYPE", "x11")
                        && (display?.StartsWith(':') == true || display?.StartsWith("unix:", StringComparison.Ordinal) == true))
                    {
                        return await RunHelperAsync("xsel", ["--clipboard", "--input"], text, "C.UTF-8", cancellationToken);
                    }
                }
            }

            if (EqualsVariable("TERM", "dumb") || HasVariable("STY"))
            {
                return new ClipboardCopyResult(
                    ClipboardCopyStatus.Failed, "This terminal does not have a supported clipboard transport. Release mouse capture and use native terminal copy.");
            }

            if (bytes > MaximumOsc52Bytes)
            {
                return TooLarge(MaximumOsc52Bytes);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string payload = Convert.ToBase64String(_utf8.GetBytes(text));
            await Task.Run(() => terminal.WriteControl($"\u001b]52;c;{payload}\u0007"), cancellationToken);
            return new ClipboardCopyResult(
                ClipboardCopyStatus.Sent, "Copy request sent to the terminal, not confirmed. Terminal or tmux permissions may prevent copying; the selection is retained.");
        }
        catch (EncoderFallbackException)
        {
            return new ClipboardCopyResult(ClipboardCopyStatus.Failed, "The selection contains invalid Unicode and cannot be copied losslessly.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 or 13 })
        {
            return new ClipboardCopyResult(ClipboardCopyStatus.Failed, "Clipboard access was denied by the operating system. The selection is retained.");
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException
            or InvalidOperationException or NotSupportedException)
        {
            // Helper output and exception messages may contain selected text; neither is exposed or logged.
            return new ClipboardCopyResult(
                ClipboardCopyStatus.Failed, "The clipboard transport failed. The selection is retained; release mouse capture to use native terminal copy.");
        }
        finally
        {
            Volatile.Write(ref _copying, 0);
        }
    }

    private async Task<ClipboardCopyResult> RunHelperAsync(
        string executable, IReadOnlyList<string> arguments, string text, string locale, CancellationToken cancellationToken)
    {
        ProcessRunRequest request = new(
            executable, arguments, WorkingDirectory: null, Timeout: TimeSpan.FromSeconds(5),
            StandardInput: text, MaximumStandardInputBytes: MaximumLocalBytes, MaximumCapturedOutputCharacters: 4096,
            EnvironmentVariables: new Dictionary<string, string?> { ["LC_CTYPE"] = locale, ["LC_ALL"] = locale });
        ProcessOutcome outcome = await _processRunner.RunAsync(request, cancellationToken);
        if (outcome.ExecutableNotFound)
        {
            return new ClipboardCopyResult(
                ClipboardCopyStatus.Failed, $"{executable} is unavailable. Nothing was copied; release mouse capture to use native terminal copy.");
        }

        if (outcome.TimedOut)
        {
            return new ClipboardCopyResult(
                ClipboardCopyStatus.Failed, "The clipboard helper timed out while accepting or draining data. Copy was not confirmed.");
        }

        if (outcome.ExitCode != 0)
        {
            return new ClipboardCopyResult(
                ClipboardCopyStatus.Failed, $"The clipboard helper failed (exit code {outcome.ExitCode?.ToString() ?? "unknown"}). The selection is retained.");
        }

        return new ClipboardCopyResult(ClipboardCopyStatus.Confirmed, "Copied to the local clipboard.");
    }

    private bool IsRemote()
    {
        return HasVariable("SSH_CONNECTION") || HasVariable("SSH_CLIENT") || HasVariable("SSH_TTY")
            || HasVariable("CODESPACES") || HasVariable("GITPOD_WORKSPACE_ID") || HasVariable("REMOTE_CONTAINERS")
            || HasVariable("REMOTE_CONTAINERS_IPC") || HasVariable("VSCODE_REMOTE_NAME")
            || HasVariable("VSCODE_AGENT_FOLDER") || HasVariable("WSL_DISTRO_NAME") || HasVariable("WSL_INTEROP")
            || HasVariable("TMUX") || HasVariable("STY") || EqualsVariable("TERM_PROGRAM", "vscode")
            || _environment.Get("SESSIONNAME")?.StartsWith("RDP-", StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool IsLocalMacTerminal() => EqualsVariable("TERM_PROGRAM", "Apple_Terminal") || EqualsVariable("TERM_PROGRAM", "iTerm.app");

    private bool HasVariable(string name) => !string.IsNullOrEmpty(_environment.Get(name));

    private bool EqualsVariable(string name, string expected) => string.Equals(_environment.Get(name), expected, StringComparison.OrdinalIgnoreCase);

    private static ClipboardCopyResult TooLarge(int limit)
    {
        return new ClipboardCopyResult(
            ClipboardCopyStatus.Failed, $"The selection exceeds this clipboard transport's {limit:N0}-byte UTF-8 limit. Nothing was copied or truncated.");
    }
}
