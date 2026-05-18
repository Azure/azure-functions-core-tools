// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Applies compact dashboard keybindings to input state.
/// </summary>
internal sealed class CompactInputController(
    CompactFunctionSearchBuilder functionSearchBuilder,
    CompactFunctionBrowserBuilder functionBrowserBuilder)
{
    private readonly CompactFunctionSearchBuilder _functionSearchBuilder = functionSearchBuilder ?? throw new ArgumentNullException(nameof(functionSearchBuilder));
    private readonly CompactFunctionBrowserBuilder _functionBrowserBuilder = functionBrowserBuilder ?? throw new ArgumentNullException(nameof(functionBrowserBuilder));

    public CompactInputResult HandleKey(
        ConsoleKeyInfo key,
        FunctionInfo[] functions,
        CompactInputState state,
        int viewportHeight,
        int logScrollStep,
        int maxLogScrollOffset)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(state);

        if (state.FunctionSearchOpen)
        {
            return HandleFunctionSearchKey(key, functions, state);
        }

        if (key.KeyChar == '?')
        {
            ToggleHelpOverlay(state);
            return Handled();
        }

        if (key.KeyChar == '/')
        {
            OpenFunctionSearch(state);
            return Handled();
        }

        switch (key.Key)
        {
            case ConsoleKey.T:
                ToggleFunctionBrowser(functions, state);
                return Handled();

            case ConsoleKey.C:
                ResetLogScroll(state);
                return new CompactInputResult(Handled: true, ClearLogsRequested: true, ShutdownRequested: false);

            case ConsoleKey.E:
                state.ErrorsOnly = !state.ErrorsOnly;
                ResetLogScroll(state);
                return Handled();

            case ConsoleKey.F:
                CycleFunctionFilter(functions, state);
                return Handled(functions.Length > 0 || state.ActiveFunctionFilter is not null);

            case ConsoleKey.D1:
            case ConsoleKey.NumPad1:
                return Handled(SetMinimumLogLevel(state, LogLevel.Information));

            case ConsoleKey.D2:
            case ConsoleKey.NumPad2:
                return Handled(SetMinimumLogLevel(state, LogLevel.Warning));

            case ConsoleKey.D3:
            case ConsoleKey.NumPad3:
                return Handled(SetMinimumLogLevel(state, LogLevel.Error));

            case ConsoleKey.Q:
                return new CompactInputResult(Handled: true, ClearLogsRequested: false, ShutdownRequested: true);

            case ConsoleKey.Escape when state.HelpOpen || state.FunctionBrowserOpen:
                state.HelpOpen = false;
                state.FunctionBrowserOpen = false;
                return Handled();

            case ConsoleKey.A when state.ActiveFunctionFilter is not null:
                state.ActiveFunctionFilter = null;
                ResetLogScroll(state);
                return Handled();

            case ConsoleKey.Enter when state.FunctionBrowserOpen && functions.Length > 0:
                state.FunctionBrowserSelectedIndex = Math.Clamp(state.FunctionBrowserSelectedIndex, 0, functions.Length - 1);
                state.ActiveFunctionFilter = functions[state.FunctionBrowserSelectedIndex].Name;
                ResetLogScroll(state);
                state.FunctionBrowserOpen = false;
                return Handled();

            case ConsoleKey.UpArrow when state.FunctionBrowserOpen:
                MoveFunctionBrowserSelection(functions, state, -1);
                return Handled();

            case ConsoleKey.DownArrow when state.FunctionBrowserOpen:
                MoveFunctionBrowserSelection(functions, state, 1);
                return Handled();

            case ConsoleKey.PageUp when state.FunctionBrowserOpen:
                MoveFunctionBrowserSelection(functions, state, -Math.Max(1, _functionBrowserBuilder.GetVisibleRows(functions.Length, viewportHeight)));
                return Handled();

            case ConsoleKey.PageDown when state.FunctionBrowserOpen:
                MoveFunctionBrowserSelection(functions, state, Math.Max(1, _functionBrowserBuilder.GetVisibleRows(functions.Length, viewportHeight)));
                return Handled();

            case ConsoleKey.PageUp when !state.HelpOpen:
                ScrollLogs(state, logScrollStep, maxLogScrollOffset);
                return Handled();

            case ConsoleKey.PageDown when !state.HelpOpen:
                return Handled(ScrollLogs(state, -logScrollStep, maxLogScrollOffset));

            case ConsoleKey.Home when state.FunctionBrowserOpen:
                state.FunctionBrowserSelectedIndex = 0;
                return Handled(functions.Length > 0);

            case ConsoleKey.End when state.FunctionBrowserOpen:
                state.FunctionBrowserSelectedIndex = Math.Max(0, functions.Length - 1);
                return Handled(functions.Length > 0);

            case ConsoleKey.Home when !state.HelpOpen:
                ScrollLogs(state, maxLogScrollOffset, maxLogScrollOffset);
                return Handled();

            case ConsoleKey.End when !state.HelpOpen:
                return Handled(ResetLogScroll(state));

            case ConsoleKey.LeftArrow when state.FunctionBrowserOpen:
                MoveFunctionBrowserSelection(functions, state, -_functionBrowserBuilder.GetTotalRows(functions.Length));
                return Handled();

            case ConsoleKey.RightArrow when state.FunctionBrowserOpen:
                MoveFunctionBrowserSelection(functions, state, _functionBrowserBuilder.GetTotalRows(functions.Length));
                return Handled();
        }

        return default;
    }

    private CompactInputResult HandleFunctionSearchKey(ConsoleKeyInfo key, FunctionInfo[] functions, CompactInputState state)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                state.FunctionSearchOpen = false;
                return Handled();

            case ConsoleKey.Enter:
            {
                FunctionInfo[] matches = _functionSearchBuilder.GetMatches(functions, state.FunctionSearchQuery);
                if (matches.Length == 0)
                {
                    return default;
                }

                state.FunctionSearchSelectedIndex = Math.Clamp(state.FunctionSearchSelectedIndex, 0, matches.Length - 1);
                state.ActiveFunctionFilter = matches[state.FunctionSearchSelectedIndex].Name;
                ResetLogScroll(state);
                state.FunctionSearchOpen = false;
                return Handled();
            }

            case ConsoleKey.UpArrow:
                MoveFunctionSearchSelection(functions, state, -1);
                return Handled();

            case ConsoleKey.DownArrow:
                MoveFunctionSearchSelection(functions, state, 1);
                return Handled();

            case ConsoleKey.Backspace when state.FunctionSearchQuery.Length > 0:
                state.FunctionSearchQuery = state.FunctionSearchQuery[..^1];
                state.FunctionSearchSelectedIndex = 0;
                state.FunctionSearchRowOffset = 0;
                return Handled();
        }

        if (!char.IsControl(key.KeyChar))
        {
            state.FunctionSearchQuery += key.KeyChar;
            state.FunctionSearchSelectedIndex = 0;
            state.FunctionSearchRowOffset = 0;
            return Handled();
        }

        return default;
    }

    private static void ToggleFunctionBrowser(FunctionInfo[] functions, CompactInputState state)
    {
        state.FunctionBrowserOpen = !state.FunctionBrowserOpen;
        if (!state.FunctionBrowserOpen)
        {
            return;
        }

        state.HelpOpen = false;
        state.FunctionSearchOpen = false;
        state.FunctionBrowserRowOffset = 0;
        if (state.ActiveFunctionFilter is null)
        {
            state.FunctionBrowserSelectedIndex = 0;
            return;
        }

        int index = Array.FindIndex(functions, f => string.Equals(f.Name, state.ActiveFunctionFilter, StringComparison.Ordinal));
        state.FunctionBrowserSelectedIndex = Math.Max(0, index);
    }

    private static void MoveFunctionBrowserSelection(FunctionInfo[] functions, CompactInputState state, int delta)
    {
        if (functions.Length == 0)
        {
            state.FunctionBrowserSelectedIndex = 0;
            state.FunctionBrowserRowOffset = 0;
            return;
        }

        state.FunctionBrowserSelectedIndex = Math.Clamp(state.FunctionBrowserSelectedIndex + delta, 0, functions.Length - 1);
    }

    private static void ToggleHelpOverlay(CompactInputState state)
    {
        state.HelpOpen = !state.HelpOpen;
        if (state.HelpOpen)
        {
            state.FunctionBrowserOpen = false;
            state.FunctionSearchOpen = false;
        }
    }

    private static void OpenFunctionSearch(CompactInputState state)
    {
        state.FunctionSearchOpen = true;
        state.HelpOpen = false;
        state.FunctionBrowserOpen = false;
        state.FunctionSearchQuery = string.Empty;
        state.FunctionSearchSelectedIndex = 0;
        state.FunctionSearchRowOffset = 0;
    }

    private void MoveFunctionSearchSelection(FunctionInfo[] functions, CompactInputState state, int delta)
    {
        FunctionInfo[] matches = _functionSearchBuilder.GetMatches(functions, state.FunctionSearchQuery);
        if (matches.Length == 0)
        {
            state.FunctionSearchSelectedIndex = 0;
            state.FunctionSearchRowOffset = 0;
            return;
        }

        state.FunctionSearchSelectedIndex = Math.Clamp(state.FunctionSearchSelectedIndex + delta, 0, matches.Length - 1);
    }

    private static void CycleFunctionFilter(FunctionInfo[] functions, CompactInputState state)
    {
        if (functions.Length == 0)
        {
            state.ActiveFunctionFilter = null;
            ResetLogScroll(state);
            return;
        }

        if (state.ActiveFunctionFilter is null)
        {
            state.ActiveFunctionFilter = functions[0].Name;
            ResetLogScroll(state);
            return;
        }

        int index = Array.FindIndex(functions, f => string.Equals(f.Name, state.ActiveFunctionFilter, StringComparison.Ordinal));
        state.ActiveFunctionFilter = index >= 0 && index < functions.Length - 1
            ? functions[index + 1].Name
            : null;
        ResetLogScroll(state);
    }

    private static bool SetMinimumLogLevel(CompactInputState state, LogLevel minimumLogLevel)
    {
        if (state.MinimumLogLevel == minimumLogLevel)
        {
            return false;
        }

        state.MinimumLogLevel = minimumLogLevel;
        ResetLogScroll(state);
        return true;
    }

    private static bool ScrollLogs(CompactInputState state, int delta, int maxLogScrollOffset)
    {
        int previous = state.LogScrollOffset;
        state.LogScrollOffset = Math.Clamp(state.LogScrollOffset + delta, 0, maxLogScrollOffset);
        return state.LogScrollOffset != previous;
    }

    private static bool ResetLogScroll(CompactInputState state)
    {
        if (state.LogScrollOffset == 0)
        {
            return false;
        }

        state.LogScrollOffset = 0;
        return true;
    }

    private static CompactInputResult Handled(bool handled = true)
        => new(handled, ClearLogsRequested: false, ShutdownRequested: false);
}
