// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class CompactInputControllerTests
{
    [Fact]
    public void HandleKey_Question_TogglesHelpAndClosesBrowser()
    {
        CompactInputController controller = CreateController();
        var state = new CompactInputState { FunctionBrowserOpen = true };

        CompactInputResult result = controller.HandleKey(
            Key('?', ConsoleKey.Oem2),
            [],
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        Assert.True(result.Handled);
        Assert.True(state.HelpOpen);
        Assert.False(state.FunctionBrowserOpen);
    }

    [Fact]
    public void HandleKey_SearchEnter_AppliesSelectedMatch()
    {
        CompactInputController controller = CreateController();
        var state = new CompactInputState { FunctionSearchOpen = true, FunctionSearchQuery = "ord" };
        FunctionInfo[] functions =
        [
            CreateFunction("HttpTrigger", "/api/hello"),
            CreateFunction("QueueProcessor", "orders"),
        ];

        CompactInputResult result = controller.HandleKey(
            Key('\r', ConsoleKey.Enter),
            functions,
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        Assert.True(result.Handled);
        Assert.False(state.FunctionSearchOpen);
        Assert.Equal("QueueProcessor", state.ActiveFunctionFilter);
    }

    [Fact]
    public void HandleKey_C_ReturnsClearLogsRequest()
    {
        CompactInputController controller = CreateController();
        var state = new CompactInputState { LogScrollOffset = 5 };

        CompactInputResult result = controller.HandleKey(
            Key('c', ConsoleKey.C),
            [],
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        Assert.True(result.Handled);
        Assert.True(result.ClearLogsRequested);
        Assert.Equal(0, state.LogScrollOffset);
    }

    [Fact]
    public void HandleKey_Digit2_SetsWarningLogLevel()
    {
        CompactInputController controller = CreateController();
        var state = new CompactInputState();

        CompactInputResult result = controller.HandleKey(
            Key('2', ConsoleKey.D2),
            [],
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        Assert.True(result.Handled);
        Assert.Equal(LogLevel.Warning, state.MinimumLogLevel);
    }

    [Fact]
    public void HandleKey_UpAndDownArrows_ScrollLogTailOneLine()
    {
        CompactInputController controller = CreateController();
        var state = new CompactInputState { LogScrollOffset = 2 };

        CompactInputResult upResult = controller.HandleKey(
            Key('\0', ConsoleKey.UpArrow),
            [],
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        CompactInputResult downResult = controller.HandleKey(
            Key('\0', ConsoleKey.DownArrow),
            [],
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        Assert.True(upResult.Handled);
        Assert.True(downResult.Handled);
        Assert.Equal(2, state.LogScrollOffset);
    }

    [Fact]
    public void HandleKey_PageUpAndPageDown_ScrollLogTailByConfiguredStep()
    {
        CompactInputController controller = CreateController();
        var state = new CompactInputState { LogScrollOffset = 2 };

        CompactInputResult pageUpResult = controller.HandleKey(
            Key('\0', ConsoleKey.PageUp),
            [],
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        CompactInputResult pageDownResult = controller.HandleKey(
            Key('\0', ConsoleKey.PageDown),
            [],
            state,
            viewportHeight: 24,
            logScrollStep: 3,
            maxLogScrollOffset: 200);

        Assert.True(pageUpResult.Handled);
        Assert.True(pageDownResult.Handled);
        Assert.Equal(2, state.LogScrollOffset);
    }

    private static CompactInputController CreateController()
    {
        var theme = new DefaultTheme();
        return new CompactInputController(
            new CompactFunctionSearchBuilder(theme, new FunctionPalette()),
            new CompactFunctionBrowserBuilder(theme, new FunctionPalette()));
    }

    private static FunctionInfo CreateFunction(string name, string? route)
        => new(
            name,
            "http",
            route,
            [],
            FunctionStatus.Ready,
            ActiveInvocations: 0,
            TotalInvocations: 0,
            TotalErrors: 0,
            LastInvocationAt: null,
            LastErrorMessage: null);

    private static ConsoleKeyInfo Key(char keyChar, ConsoleKey key)
        => new(keyChar, key, shift: false, alt: false, control: false);
}
