// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Spectre.Console;
using Spectre.Console.Rendering;

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
        output.Should().Contain("⚡ Azure Functions CLI");
        output.Should().Contain("Host: 4.834.0");
        output.Should().Contain("Profile: flex");
        output.Should().Contain("Stack: .NET");
    }

    [Fact]
    public void BuildFooter_WhenManyFunctionsLoaded_ShowsControls()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildFooter", snapshot, null));

        string output = writer.ToString();
        output.Should().Contain("12 functions");
        output.Should().Contain("↑/↓, PgUp/PgDn logs");
        output.Should().NotContain("Fn+↑/↓");
        output.Should().NotContain("Ctrl+U/D");
        output.Should().Contain("L:info");
        output.Should().Contain("q/Ctrl+C");
    }

    [Fact]
    public void BuildFooter_WhenHelpOpen_ShowsHelpCloseControls()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_helpOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildFooter", snapshot, null));

        string output = writer.ToString();
        output.Should().Contain("?/Esc close");
    }

    [Fact]
    public void BuildHeader_WhenHelpOpen_RendersHelpOverlay()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_helpOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        output.Should().Contain("Help");
        output.Should().Contain("Available compact-mode controls");
        output.Should().Contain("Toggle this help panel");
        output.Should().Contain("Search functions by name");
        output.Should().Contain("Scroll logs line by line");
        output.Should().Contain("Clear visible log output");
        output.Should().Contain("Cycle the active function filter");
        output.Should().Contain("Toggle errors-only log view");
        output.Should().Contain("Set visible log level");
        output.Should().Contain("Scroll logs");
        output.Should().Contain("PgUp/PgDn");
        output.Should().NotContain("Fn+↑/↓");
        output.Should().NotContain("Ctrl+U/D");
        output.Should().NotContain("Open the configured log file");
        output.Should().NotContain("Coming soon");
        output.Should().NotContain("c clear logs");
        output.Should().NotContain("e errors only");
        output.Should().NotContain("1/2/3 log level");
        output.Should().NotContain("q quit");
        output.Should().NotContain("/ search");
    }

    [Fact]
    public void BuildLayout_WhenHelpOpen_ReservesEnoughRowsForPanelHeader()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_helpOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        output.Should().Contain("Help");
        (CountRenderedLines(output) <= console.Profile.Height).Should().BeTrue();
    }

    [Fact]
    public void BuildHeader_WhenFunctionSearchOpen_RendersSearchOverlay()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_functionSearchOpen", true);
        SetPrivate(renderer, "_functionSearchQuery", "extra2");

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        output.Should().Contain("Search functions");
        output.Should().Contain("extra2");
        output.Should().Contain("HttpExtra2");
        output.Should().NotContain("HttpExtra1 ");
    }

    [Fact]
    public void BuildLayout_WhenFunctionSearchOpen_ReservesEnoughRowsForPanelHeader()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_functionSearchOpen", true);
        SetPrivate(renderer, "_functionSearchQuery", "extra");

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        output.Should().Contain("Search functions");
        (CountRenderedLines(output) <= console.Profile.Height).Should().BeTrue();
    }

    [Fact]
    public void BuildHeader_WhenFunctionBrowserOpen_RendersTwoColumnBrowser()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_functionBrowserOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        output.Should().Contain("Functions (12)");
        output.Should().Contain("HttpExtra1");
        output.Should().Contain("HttpExtra7");
        output.Should().Contain("Up/Down navigate");
        output.Should().Contain("Enter filter");
    }

    [Fact]
    public void BuildHeader_WhenIntermediateFunctionCount_RendersPriorityTruncatedTable()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildStateWithPriorityFunctions().Snapshot();

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        output.Should().Contain("HttpExtra3");
        output.Should().Contain("◉ Active");
        output.Should().Contain("HttpExtra2");
        output.Should().Contain("✗ Error");
        output.Should().Contain("HttpExtra1");
        output.Should().Contain("+7 more");
        output.Should().Contain("press t to view all");
        (output.IndexOf("HttpExtra3", StringComparison.Ordinal) < output.IndexOf("HttpExtra2", StringComparison.Ordinal)).Should().BeTrue();
        (output.IndexOf("HttpExtra2", StringComparison.Ordinal) < output.IndexOf("HttpExtra1", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public void BuildHeader_WhenFunctionCountTooLarge_RendersStatusStrip()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 20);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        output.Should().Contain("20 functions");
        output.Should().Contain("ready");
        output.Should().NotContain("press t to view all");
    }

    [Fact]
    public void HandleKey_EnterInFunctionBrowser_AppliesSelectedFunctionFilter()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        var state = BuildState(functionCount: 12);
        SetPrivate(renderer, "_state", state);

        InvokePrivate<bool>(renderer, "HandleKey", Key('t', ConsoleKey.T)).Should().BeTrue();
        ((bool)GetPrivate(renderer, "_functionBrowserOpen")!).Should().BeTrue();

        InvokePrivate<bool>(renderer, "HandleKey", Key('\0', ConsoleKey.DownArrow)).Should().BeTrue();
        InvokePrivate<bool>(renderer, "HandleKey", Key('\r', ConsoleKey.Enter)).Should().BeTrue();

        ((bool)GetPrivate(renderer, "_functionBrowserOpen")!).Should().BeFalse();
        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpExtra2");
    }

    [Fact]
    public void HandleKey_Question_TogglesHelpOverlayAndClosesFunctionBrowser()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_functionBrowserOpen", true);

        InvokePrivate<bool>(renderer, "HandleKey", Key('?', ConsoleKey.Oem2)).Should().BeTrue();

        ((bool)GetPrivate(renderer, "_helpOpen")!).Should().BeTrue();
        ((bool)GetPrivate(renderer, "_functionBrowserOpen")!).Should().BeFalse();

        InvokePrivate<bool>(renderer, "HandleKey", Key('?', ConsoleKey.Oem2)).Should().BeTrue();

        ((bool)GetPrivate(renderer, "_helpOpen")!).Should().BeFalse();
    }

    [Fact]
    public void HandleKey_Escape_ClosesHelpOverlay()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_helpOpen", true);

        InvokePrivate<bool>(renderer, "HandleKey", Key('\u001b', ConsoleKey.Escape)).Should().BeTrue();

        ((bool)GetPrivate(renderer, "_helpOpen")!).Should().BeFalse();
    }

    [Fact]
    public void HandleKey_A_ClearsActiveFunctionFilter()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_activeFunctionFilter", "HttpExtra1");

        InvokePrivate<bool>(renderer, "HandleKey", Key('a', ConsoleKey.A)).Should().BeTrue();

        GetPrivate(renderer, "_activeFunctionFilter").Should().BeNull();
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
        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpTrigger1");
        output.Should().Contain("Filter HttpTrigger1");
        output.Should().Contain("selected before recycle");
        output.Should().Contain("selected after recycle");
        output.Should().NotContain("other before recycle");
        output.Should().NotContain("other after recycle");
    }

    [Fact]
    public async Task HandleKey_C_ClearsVisibleLogTail()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        await renderer.OnEventAsync(Log("HttpTrigger1", "first compact log"), [], CancellationToken.None);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        writer.ToString().Should().Contain("first compact log");

        InvokePrivate<bool>(renderer, "HandleKey", Key('c', ConsoleKey.C)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        output.Should().NotContain("first compact log");
        output.Should().Contain("Waiting for events");
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
        output.Should().Contain("info compact log");
        output.Should().Contain("error compact log");

        InvokePrivate<bool>(renderer, "HandleKey", Key('e', ConsoleKey.E)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        output.Should().NotContain("info compact log");
        output.Should().Contain("error compact log");
        output.Should().Contain("Errors only");

        InvokePrivate<bool>(renderer, "HandleKey", Key('e', ConsoleKey.E)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        output.Should().Contain("info compact log");
        output.Should().Contain("error compact log");
        output.Should().NotContain("Errors only");
    }

    [Fact]
    public void HandleKey_F_CyclesFunctionFilterInDeterministicOrderThenAllFunctions()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));

        InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)).Should().BeTrue();
        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpExtra1");

        InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)).Should().BeTrue();
        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpExtra2");

        InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)).Should().BeTrue();
        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpTrigger1");

        InvokePrivate<bool>(renderer, "HandleKey", Key('f', ConsoleKey.F)).Should().BeTrue();
        GetPrivate(renderer, "_activeFunctionFilter").Should().BeNull();
    }

    [Fact]
    public async Task HandleKey_123_SetVisibleLogLevelFilter()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        await renderer.OnEventAsync(Log("HttpTrigger1", "info compact log"), [], CancellationToken.None);
        await renderer.OnEventAsync(Log("HttpTrigger1", "warning compact log", LogLevel.Warning), [], CancellationToken.None);
        await renderer.OnEventAsync(Log("HttpTrigger1", "error compact log", LogLevel.Error), [], CancellationToken.None);

        InvokePrivate<bool>(renderer, "HandleKey", Key('2', ConsoleKey.D2)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        output.Should().NotContain("info compact log");
        output.Should().Contain("warning compact log");
        output.Should().Contain("error compact log");
        output.Should().Contain("L:warn");

        InvokePrivate<bool>(renderer, "HandleKey", Key('3', ConsoleKey.D3)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        output.Should().NotContain("info compact log");
        output.Should().NotContain("warning compact log");
        output.Should().Contain("error compact log");
        output.Should().Contain("L:error");

        InvokePrivate<bool>(renderer, "HandleKey", Key('1', ConsoleKey.D1)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        output.Should().Contain("info compact log");
        output.Should().Contain("warning compact log");
        output.Should().Contain("error compact log");
        output.Should().Contain("L:info");
    }

    [Fact]
    public void HandleKey_Q_RequestsShutdown()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        bool requested = false;
        renderer.ShutdownRequested += () => requested = true;

        InvokePrivate<bool>(renderer, "HandleKey", Key('q', ConsoleKey.Q)).Should().BeTrue();

        requested.Should().BeTrue();
    }

    [Fact]
    public void HandleKey_L_IsNotHandled()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));

        InvokePrivate<bool>(renderer, "HandleKey", Key('l', ConsoleKey.L)).Should().BeFalse();
    }

    [Fact]
    public async Task HandleKey_PageUpPageDownAndEnd_ScrollLogTail()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        for (int i = 1; i <= 20; i++)
        {
            await renderer.OnEventAsync(Log("HttpTrigger1", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"compact log {i:00}")), [], CancellationToken.None);
        }

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        output.Should().NotContain("compact log 01");
        output.Should().Contain("compact log 20");

        InvokePrivate<bool>(renderer, "HandleKey", Key('\0', ConsoleKey.PageUp)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        output.Should().Contain("compact log 01");
        output.Should().NotContain("compact log 20");
        output.Should().Contain("Scrollback");

        await renderer.OnEventAsync(Log("HttpTrigger1", "compact log 21"), [], CancellationToken.None);
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        output.Should().Contain("compact log 01");
        output.Should().NotContain("compact log 21");

        InvokePrivate<bool>(renderer, "HandleKey", Key('\0', ConsoleKey.End)).Should().BeTrue();
        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        output = writer.ToString();
        output.Should().NotContain("compact log 01");
        output.Should().Contain("compact log 21");

        InvokePrivate<bool>(renderer, "HandleKey", Key('\0', ConsoleKey.PageUp)).Should().BeTrue();
        InvokePrivate<bool>(renderer, "HandleKey", Key('\0', ConsoleKey.PageDown)).Should().BeTrue();
    }

    [Fact]
    public async Task BuildLayout_WhenLogLineWraps_KeepsLayoutWithinViewport()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer();
        console.Profile.Height = 18;
        SetPrivate(renderer, "_state", BuildState(functionCount: 3));
        await renderer.OnEventAsync(Log("HttpTrigger1", new string('x', 500)), [], CancellationToken.None);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildLayout"));

        string output = writer.ToString();
        output.Should().Contain("Function");
        (CountRenderedLines(output) <= console.Profile.Height).Should().BeTrue();
    }

    [Fact]
    public void HandleKey_SlashSearchEnter_AppliesSelectedMatch()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));

        InvokePrivate<bool>(renderer, "HandleKey", Key('/', ConsoleKey.Oem2)).Should().BeTrue();
        foreach (char c in "extra2")
        {
            InvokePrivate<bool>(renderer, "HandleKey", Key(c, CharToConsoleKey(c))).Should().BeTrue();
        }

        InvokePrivate<bool>(renderer, "HandleKey", Key('\r', ConsoleKey.Enter)).Should().BeTrue();

        ((bool)GetPrivate(renderer, "_functionSearchOpen")!).Should().BeFalse();
        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpExtra2");
    }

    [Fact]
    public void HandleKey_SlashSearchDownEnter_AppliesSelectedSearchResult()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));

        InvokePrivate<bool>(renderer, "HandleKey", Key('/', ConsoleKey.Oem2)).Should().BeTrue();
        foreach (char c in "extra")
        {
            InvokePrivate<bool>(renderer, "HandleKey", Key(c, CharToConsoleKey(c))).Should().BeTrue();
        }

        InvokePrivate<bool>(renderer, "HandleKey", Key('\0', ConsoleKey.DownArrow)).Should().BeTrue();
        InvokePrivate<bool>(renderer, "HandleKey", Key('\r', ConsoleKey.Enter)).Should().BeTrue();

        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpExtra2");
    }

    [Fact]
    public void HandleKey_SlashSearchEscape_CancelsSearchWithoutChangingFilter()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));
        SetPrivate(renderer, "_activeFunctionFilter", "HttpTrigger1");

        InvokePrivate<bool>(renderer, "HandleKey", Key('/', ConsoleKey.Oem2)).Should().BeTrue();
        InvokePrivate<bool>(renderer, "HandleKey", Key('x', ConsoleKey.X)).Should().BeTrue();
        InvokePrivate<bool>(renderer, "HandleKey", Key('\u001b', ConsoleKey.Escape)).Should().BeTrue();

        ((bool)GetPrivate(renderer, "_functionSearchOpen")!).Should().BeFalse();
        GetPrivate(renderer, "_activeFunctionFilter").Should().Be("HttpTrigger1");
    }

    [Fact]
    public void HandleKey_SlashSearchEnterWithNoMatches_KeepsSearchOpen()
    {
        (CompactRenderer renderer, _, _) = NewRenderer();
        SetPrivate(renderer, "_state", BuildState(functionCount: 12));

        InvokePrivate<bool>(renderer, "HandleKey", Key('/', ConsoleKey.Oem2)).Should().BeTrue();
        foreach (char c in "zzz")
        {
            InvokePrivate<bool>(renderer, "HandleKey", Key(c, CharToConsoleKey(c))).Should().BeTrue();
        }

        InvokePrivate<bool>(renderer, "HandleKey", Key('\r', ConsoleKey.Enter)).Should().BeFalse();

        ((bool)GetPrivate(renderer, "_functionSearchOpen")!).Should().BeTrue();
        GetPrivate(renderer, "_activeFunctionFilter").Should().BeNull();
    }

    [Fact]
    public void BuildFooter_OnMacOS_ShowsMacOSNavigationControls()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer(isMacOS: true);
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildFooter", snapshot, null));

        string output = writer.ToString();
        output.Should().Contain("↑/↓, Fn+↑/↓ logs");
        output.Should().NotContain("PgUp/PgDn");
        output.Should().NotContain("Ctrl+U/D");
    }

    [Fact]
    public void BuildHeader_WhenHelpOpenOnMacOS_ShowsMacOSNavigationControls()
    {
        (CompactRenderer renderer, IAnsiConsole console, StringWriter writer) = NewRenderer(isMacOS: true);
        DashboardSnapshot snapshot = BuildSnapshot(functionCount: 12);
        SetPrivate(renderer, "_helpOpen", true);

        Render(console, writer, InvokePrivate<IRenderable>(renderer, "BuildHeader", snapshot));

        string output = writer.ToString();
        output.Should().Contain("Fn+↑/↓");
        output.Should().NotContain("PgUp/PgDn");
        output.Should().NotContain("Ctrl+U/D");
    }

    [Fact]
    public async Task OnStartAsync_WhenMacOSAndAlternateBufferSupported_UsesAlternateScreen()
    {
        (CompactRenderer renderer, _, StringWriter writer) = NewRenderer(isMacOS: true, ansi: true, alternateBuffer: true);

        await renderer.OnStartAsync(BuildState(functionCount: 1), CancellationToken.None);
        await renderer.OnSummaryAsync(CreateSummary(), CancellationToken.None);

        string output = writer.ToString();
        int enterIndex = output.IndexOf("\u001b[?1049h\u001b[H", StringComparison.Ordinal);
        int exitIndex = output.IndexOf("\u001b[?1049l", StringComparison.Ordinal);
        enterIndex.Should().NotBe(-1);
        exitIndex.Should().NotBe(-1);
        (enterIndex < exitIndex).Should().BeTrue();
    }

    [Fact]
    public async Task OnStartAsync_WhenNotMacOS_DoesNotUseAlternateScreen()
    {
        (CompactRenderer renderer, _, StringWriter writer) = NewRenderer(isMacOS: false, ansi: true, alternateBuffer: true);

        await renderer.OnStartAsync(BuildState(functionCount: 1), CancellationToken.None);
        await renderer.OnSummaryAsync(CreateSummary(), CancellationToken.None);

        string output = writer.ToString();
        output.Should().NotContain("\u001b[?1049h");
        output.Should().NotContain("\u001b[?1049l");
    }

    [Fact]
    public async Task OnStartAsync_WhenMacOSWithoutAlternateBuffer_DoesNotUseAlternateScreen()
    {
        (CompactRenderer renderer, _, StringWriter writer) = NewRenderer(isMacOS: true, ansi: true, alternateBuffer: false);

        await renderer.OnStartAsync(BuildState(functionCount: 1), CancellationToken.None);
        await renderer.OnSummaryAsync(CreateSummary(), CancellationToken.None);

        string output = writer.ToString();
        output.Should().NotContain("\u001b[?1049h");
        output.Should().NotContain("\u001b[?1049l");
    }

    private static (CompactRenderer Renderer, IAnsiConsole Console, StringWriter Writer) NewRenderer(
        DashboardRunInfo? runInfo = null,
        bool isMacOS = false,
        bool ansi = false,
        bool alternateBuffer = false)
    {
        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 120;
        console.Profile.Height = 24;
        console.Profile.Capabilities.Ansi = ansi;
        console.Profile.Capabilities.AlternateBuffer = alternateBuffer;

        IInteractionService interaction = new SpectreInteractionService(new DefaultTheme(), console, console);
        IPlatform platform = Substitute.For<IPlatform>();
        platform.IsMacOS.Returns(isMacOS);
        CompactDashboardShortcutLabels shortcutLabels = new(platform);
        var renderer = new CompactRenderer(interaction, new FunctionPalette(), shortcutLabels, platform, console, runInfo);
        return (renderer, console, writer);
    }

    private static void Render(IAnsiConsole console, StringWriter writer, IRenderable renderable)
    {
        writer.GetStringBuilder().Clear();
        console.Write(renderable);
    }

    private static int CountRenderedLines(string output)
    {
        string normalized = output.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        return normalized.Length == 0
            ? 0
            : normalized.Split('\n').Length;
    }

    private static DashboardSnapshot BuildSnapshot(int functionCount)
        => BuildState(functionCount).Snapshot();

    private static SummaryEvent CreateSummary()
        => new(
            DateTimeOffset.UtcNow,
            "stopped",
            UptimeSeconds: 1,
            FunctionCount: 1,
            TotalInvocations: 0,
            SucceededInvocations: 0,
            FailedInvocations: 0,
            ErrorCount: 0);

    private static DashboardState BuildStateWithPriorityFunctions()
    {
        var state = BuildState(functionCount: 12);
        state.Observe(InvocationStarted("HttpExtra3", "active-1"));
        state.Observe(InvocationStarted("HttpExtra1", "recent-1"));
        state.Observe(InvocationCompleted("HttpExtra1", "recent-1", "succeeded"));
        state.Observe(InvocationCompleted("HttpExtra2", "failed-1", "failed"));
        return state;
    }

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

    private static HostLogEntry InvocationStarted(string name, string invocationId)
        => MakeEntry(
            $"Function.{name}",
            new()
            {
                [HostLogAttributeKeys.FunctionName] = name,
                [HostLogAttributeKeys.FunctionInvocationId] = invocationId,
            });

    private static HostLogEntry InvocationCompleted(string name, string invocationId, string result)
        => MakeEntry(
            $"Function.{name}",
            new()
            {
                [HostLogAttributeKeys.FunctionName] = name,
                [HostLogAttributeKeys.FunctionInvocationId] = invocationId,
                [HostLogAttributeKeys.FunctionResult] = result,
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

    private static ConsoleKey CharToConsoleKey(char value)
        => Enum.Parse<ConsoleKey>(value.ToString().ToUpperInvariant());

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
