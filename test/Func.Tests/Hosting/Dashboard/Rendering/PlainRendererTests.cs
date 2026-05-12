// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class PlainRendererTests
{
    [Fact]
    public async Task HttpFunctionRoute_RendersFullClickableUrl_WhenTerminalSupportsLinks()
    {
        (PlainRenderer renderer, StringWriter writer, IAnsiConsole console) = NewRenderer();
        console.Profile.Capabilities.Links = true;

        await PumpScenarioAsync(renderer);

        string output = writer.ToString();
        const string url = "http://localhost:7071/api/hello";
        const string expectedOsc8Open = "\u001b]8;;" + url + "\u001b\\";
        const string expectedOsc8Close = "\u001b]8;;\u001b\\";

        Assert.Contains(expectedOsc8Open, output);
        Assert.Contains(expectedOsc8Close, output);
        Assert.Contains("GET,POST " + expectedOsc8Open + url + expectedOsc8Close, output);
    }

    [Fact]
    public async Task HttpFunctionRoute_RendersPlainUrl_WhenTerminalDoesNotSupportLinks()
    {
        (PlainRenderer renderer, StringWriter writer, IAnsiConsole console) = NewRenderer();
        console.Profile.Capabilities.Links = false;

        await PumpScenarioAsync(renderer);

        string output = writer.ToString();
        Assert.Contains("GET,POST http://localhost:7071/api/hello", output);
        Assert.DoesNotContain("\u001b]8;", output);
    }

    [Fact]
    public async Task NonHttpFunctionRoute_NotRenderedAsHyperlink()
    {
        (PlainRenderer renderer, StringWriter writer, IAnsiConsole console) = NewRenderer();
        console.Profile.Capabilities.Links = true;

        var state = new DashboardState();
        await renderer.OnStartAsync(state, default);
        await PumpHostReadyAsync(renderer, state);

        HostLogEntry entry = MakeEntry(new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
            [HostLogAttributeKeys.FunctionName] = "QueueProcessor",
            [HostLogAttributeKeys.FunctionTriggerType] = "queue",
            [HostLogAttributeKeys.FunctionRoute] = "my-queue",
        });
        await renderer.OnEventAsync(entry, state.Observe(entry), default);

        string output = writer.ToString();
        Assert.Contains("QueueProcessor", output);
        Assert.Contains("my-queue", output);
        Assert.DoesNotContain("\u001b]8;", output);
    }

    private static async Task PumpScenarioAsync(PlainRenderer renderer)
    {
        var state = new DashboardState();
        await renderer.OnStartAsync(state, default);
        await PumpHostReadyAsync(renderer, state);

        HostLogEntry entry = MakeEntry(new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
            [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
            [HostLogAttributeKeys.FunctionTriggerType] = "http",
            [HostLogAttributeKeys.FunctionRoute] = "/api/hello",
            [HostLogAttributeKeys.FunctionHttpMethods] = new[] { "GET", "POST" },
        });
        await renderer.OnEventAsync(entry, state.Observe(entry), default);
    }

    private static async Task PumpHostReadyAsync(PlainRenderer renderer, DashboardState state)
    {
        HostLogEntry boot = MakeEntry(new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
            [HostLogAttributeKeys.HostState] = "ready",
            [HostLogAttributeKeys.HostListenUri] = "http://localhost:7071",
        });
        await renderer.OnEventAsync(boot, state.Observe(boot), default);
    }

    private static (PlainRenderer renderer, StringWriter writer, IAnsiConsole console) NewRenderer()
    {
        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        var interaction = new SpectreInteractionService(new DefaultTheme(), console, console);
        return (new PlainRenderer(interaction, console), writer, console);
    }

    private static HostLogEntry MakeEntry(Dictionary<string, object?> attrs)
        => new(DateTimeOffset.UtcNow, "Host.Lifecycle", LogLevel.Information, default, "msg", null, attrs);
}
