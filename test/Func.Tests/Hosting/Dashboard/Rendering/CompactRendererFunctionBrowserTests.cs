// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class CompactRendererFunctionBrowserTests
{
    [Fact]
    public void BuildHeader_IncludesProfileAndStackMetadata()
    {
        var runInfo = new DashboardRunInfo(ProfileName: "flex", StackName: ".NET");
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer(runInfo);
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        Assert.Contains("⚡ Azure Functions CLI", output);
        Assert.Contains("Host: 4.834.0", output);
        Assert.Contains("Profile: flex", output);
        Assert.Contains("Stack: .NET", output);
    }

    [Fact]
    public void BuildFooter_WhenManyFunctionsLoaded_ShowsControls()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildFooter", snapshot, null));

        string output = writer.ToString();
        Assert.Contains("12 functions", output);
        Assert.Contains("t functions", output);
        Assert.Contains("f filter", output);
        Assert.Contains("c clear", output);
        Assert.Contains("e errors", output);
        Assert.Contains("L:info", output);
        Assert.Contains("Ctrl+C", output);
    }

    [Fact]
    public void BuildFooter_WhenHelpOpen_ShowsHelpCloseControls()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_helpOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildFooter", snapshot, null));

        string output = writer.ToString();
        Assert.Contains("? close", output);
        Assert.Contains("Esc close", output);
    }

    [Fact]
    public void BuildHeader_WhenHelpOpen_RendersHelpOverlay()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_helpOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        Assert.Contains("Help", output);
        Assert.Contains("Available compact-mode controls", output);
        Assert.Contains("Toggle this help panel", output);
        Assert.Contains("Clear visible log output", output);
        Assert.Contains("Cycle the active function filter", output);
        Assert.Contains("Toggle errors-only log view", output);
        Assert.Contains("Set visible log level", output);
        Assert.Contains("Coming soon", output);
        Assert.DoesNotContain("c clear logs", output);
        Assert.DoesNotContain("e errors only", output);
        Assert.DoesNotContain("1/2/3 log level", output);
    }

    [Fact]
    public void BuildHeader_WhenFunctionBrowserOpen_RendersTwoColumnBrowser()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_functionBrowserOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        Assert.Contains("Functions (12)", output);
        Assert.Contains("HttpExtra1", output);
        Assert.Contains("HttpExtra7", output);
        Assert.Contains("Up/Down navigate", output);
        Assert.Contains("Enter filter", output);
    }

    [Fact]
    public void HandleKey_EnterInFunctionBrowser_AppliesSelectedFunctionFilter()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        var state = BuildState(functionCount: 12);
        SetPrivate(renderer, "_state", state);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('t', ConsoleKey.T)));
        Assert.True((bool)GetPrivate(renderer, "_functionBrowserOpen")!);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('\0', ConsoleKey.DownArrow)));
        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('\r', ConsoleKey.Enter)));

        Assert.False((bool)GetPrivate(renderer, "_functionBrowserOpen")!);
        Assert.Equal("HttpExtra2", GetPrivate(renderer, "_activeFunctionFilter"));
    }

    [Fact]
    public void HandleKey_Question_TogglesHelpOverlayAndClosesFunctionBrowser()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_functionBrowserOpen", true);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('?', ConsoleKey.Oem2)));

        Assert.True((bool)GetPrivate(renderer, "_helpOpen")!);
        Assert.False((bool)GetPrivate(renderer, "_functionBrowserOpen")!);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('?', ConsoleKey.Oem2)));

        Assert.False((bool)GetPrivate(renderer, "_helpOpen")!);
    }

    [Fact]
    public void HandleKey_Escape_ClosesHelpOverlay()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_helpOpen", true);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('\u001b', ConsoleKey.Escape)));

        Assert.False((bool)GetPrivate(renderer, "_helpOpen")!);
    }

    [Fact]
    public void HandleKey_A_ClearsActiveFunctionFilter()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_activeFunctionFilter", "HttpExtra1");

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('a', ConsoleKey.A)));

        Assert.Null(GetPrivate(renderer, "_activeFunctionFilter"));
    }

    [Fact]
    public async Task BuildLayout_WhenHostRecycles_PreservesActiveFunctionFilterAfterRediscovery()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        var state = BuildState(functionCount: 3);
        SetPrivate(renderer, "_state", state);
        SetPrivate(renderer, "_activeFunctionFilter", "HttpTrigger1");
        await renderer.OnEventAsync(Log("HttpTrigger1", "selected before recycle"), [], CancellationToken.None);
        await renderer.OnEventAsync(Log("HttpExtra1", "other before recycle"), [], CancellationToken.None);

        state.Observe(HostState("recycling"));
        state.Observe(HostState("ready"));
        state.Observe(Discover("HttpTrigger1", "/api/hello"));
        state.Observe(Discover("HttpExtra1", "/api/extra-1"));
        await renderer.OnEventAsync(Log("HttpTrigger1", "selected after recycle"), [], CancellationToken.None);
        await renderer.OnEventAsync(Log("HttpExtra1", "other after recycle"), [], CancellationToken.None);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        Assert.Equal("HttpTrigger1", GetPrivate(renderer, "_activeFunctionFilter"));
        Assert.Contains("Filter HttpTrigger1", output);
        Assert.Contains("selected before recycle", output);
        Assert.Contains("selected after recycle", output);
        Assert.DoesNotContain("other before recycle", output);
        Assert.DoesNotContain("other after recycle", output);
    }

    [Fact]
    public async Task HandleKey_C_ClearsVisibleLogTail()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        await renderer.OnEventAsync(Log("HttpTrigger1", "first compact log"), [], CancellationToken.None);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        Assert.Contains("first compact log", writer.ToString());

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('c', ConsoleKey.C)));
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        Assert.DoesNotContain("first compact log", output);
        Assert.Contains("Waiting for events", output);
    }

    [Fact]
    public async Task HandleKey_E_TogglesErrorsOnlyLogView()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        await renderer.OnEventAsync(Log("HttpTrigger1", "info compact log"), [], CancellationToken.None);
        await renderer.OnEventAsync(Log("HttpTrigger1", "error compact log", LogLevel.Error), [], CancellationToken.None);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        Assert.Contains("info compact log", output);
        Assert.Contains("error compact log", output);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('e', ConsoleKey.E)));
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        Assert.DoesNotContain("info compact log", output);
        Assert.Contains("error compact log", output);
        Assert.Contains("Errors only", output);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('e', ConsoleKey.E)));
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        Assert.Contains("info compact log", output);
        Assert.Contains("error compact log", output);
        Assert.DoesNotContain("Errors only", output);
    }

    [Fact]
    public void HandleKey_F_CyclesFunctionFilterInDeterministicOrderThenAllFunctions()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)));
        Assert.Equal("HttpExtra1", GetPrivate(renderer, "_activeFunctionFilter"));

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)));
        Assert.Equal("HttpExtra2", GetPrivate(renderer, "_activeFunctionFilter"));

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)));
        Assert.Equal("HttpTrigger1", GetPrivate(renderer, "_activeFunctionFilter"));

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)));
        Assert.Null(GetPrivate(renderer, "_activeFunctionFilter"));
    }

    [Fact]
    public async Task HandleKey_123_SetVisibleLogLevelFilter()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        await renderer.OnEventAsync(Log("HttpTrigger1", "info compact log"), [], CancellationToken.None);
        await renderer.OnEventAsync(Log("HttpTrigger1", "warning compact log", LogLevel.Warning), [], CancellationToken.None);
        await renderer.OnEventAsync(Log("HttpTrigger1", "error compact log", LogLevel.Error), [], CancellationToken.None);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('2', ConsoleKey.D2)));
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        Assert.DoesNotContain("info compact log", output);
        Assert.Contains("warning compact log", output);
        Assert.Contains("error compact log", output);
        Assert.Contains("L:warn", output);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('3', ConsoleKey.D3)));
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        Assert.DoesNotContain("info compact log", output);
        Assert.DoesNotContain("warning compact log", output);
        Assert.Contains("error compact log", output);
        Assert.Contains("L:error", output);

        Assert.True(InvokePrivate<bool>(renderer, "HandleKey", Key('1', ConsoleKey.D1)));
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        Assert.Contains("info compact log", output);
        Assert.Contains("warning compact log", output);
        Assert.Contains("error compact log", output);
        Assert.Contains("L:info", output);
    }

    private static (CompactRenderer Renderer, IAnsiConsole Console, StringWriter Writer) NewRenderer(DashboardRunInfo? runInfo = null)
    {
        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 120;
        console.Profile.Height = 24;

        IInteractionService interaction = new SpectreInteractionService(new DefaultTheme(), console, console);
        return (new CompactRenderer(interaction, new FunctionPalette(), console, runInfo), console, writer);
    }

    private static void Render(IAnsiConsole console, StringWriter writer, IRenderable renderable)
    {
        writer.GetStringBuilder().Clear();
        console.Write(renderable);
    }

    private static DashboardSnapshot BuildSnapshot(int functionCount)
        => BuildState(functionCount).Snapshot();

    private static DashboardState BuildState(int functionCount)
    {
        var state = new DashboardState();
        state.Observe(MakeEntry(
            "Host.Lifecycle",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "ready",
                [HostLogAttributeKeys.HostVersion] = "4.834.0",
                [HostLogAttributeKeys.HostListenUri] = "http://localhost:7071",
            }));

        state.Observe(Discover("HttpTrigger1", "/api/hello"));
        for (int i = 1; i <= functionCount - 1; i++)
        {
            state.Observe(Discover($"HttpExtra{i}", $"/api/extra-{i}"));
        }

        return state;
    }

    private static HostLogEntry Discover(string name, string route)
        => MakeEntry(
            "Host.Indexer",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
                [HostLogAttributeKeys.FunctionName] = name,
                [HostLogAttributeKeys.FunctionTriggerType] = "http",
                [HostLogAttributeKeys.FunctionRoute] = route,
                [HostLogAttributeKeys.FunctionHttpMethods] = new[] { "GET" },
            });

    private static HostLogEntry HostState(string state)
        => MakeEntry(
            "Host.Lifecycle",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = state,
            });

    private static HostLogEntry MakeEntry(string category, Dictionary<string, object?> attrs)
        => new(DateTimeOffset.UtcNow, category, LogLevel.Information, default, "msg", null, attrs);

    private static HostLogEntry Log(string functionName, string message, LogLevel level = LogLevel.Information)
        => new(
            DateTimeOffset.UtcNow,
            $"Function.{functionName}",
            level,
            default,
            message,
            null,
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.FunctionName] = functionName,
            });

    private static ConsoleKeyInfo Key(char keyChar, ConsoleKey key)
        => new(keyChar, key, shift: false, alt: false, control: false);

    private static T InvokePrivate<T>(object instance, string name, params object?[] args)
    {
        MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().FullName, name);

        return (T)method.Invoke(instance, args)!;
    }

    private static object? GetPrivate(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);

        return field.GetValue(instance);
    }

    private static void SetPrivate(object instance, string name, object? value)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);

        field.SetValue(instance, value);
    }
}
