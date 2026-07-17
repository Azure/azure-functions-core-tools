// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using NSubstitute;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class HttpRouteFormatterTests
{
    private readonly ITheme _theme = Substitute.For<ITheme>();

    public HttpRouteFormatterTests()
    {
        _theme.Hyperlink.Returns(new Style(Color.Blue, decoration: Decoration.Underline));
    }

    [Fact]
    public void HttpFunction_WithListenUri_EmitsLinkMarkup()
    {
        var fn = MakeFunction(triggerType: "http", route: "/api/hello", methods: ["GET", "POST"]);

        string result = HttpRouteFormatter.FormatRouteMarkup(fn, "http://localhost:7071", _theme);

        const string url = "http://localhost:7071/api/hello";
        result.Should().Be($"GET,POST [underline blue link={url}]{url}[/]");
    }

    [Fact]
    public void HttpFunction_NormalizesSlashesBetweenBaseAndRoute()
    {
        var fn = MakeFunction(triggerType: "http", route: "api/orders", methods: ["GET"]);

        string result = HttpRouteFormatter.FormatRouteMarkup(fn, "http://localhost:7071/", _theme);

        result.Should().Contain("[underline blue link=http://localhost:7071/api/orders]");
    }

    [Fact]
    public void HttpFunction_WithoutListenUri_FallsBackToPlainText()
    {
        var fn = MakeFunction(triggerType: "http", route: "/api/hello", methods: ["GET"]);

        string result = HttpRouteFormatter.FormatRouteMarkup(fn, listenUri: null, _theme);

        result.Should().Be("GET /api/hello");
        result.Should().NotContain("[link=");
    }

    [Fact]
    public void NonHttpFunction_NeverEmitsLink()
    {
        var fn = MakeFunction(triggerType: "queue", route: "my-queue", methods: []);

        string result = HttpRouteFormatter.FormatRouteMarkup(fn, "http://localhost:7071", _theme);

        result.Should().Be("my-queue");
        result.Should().NotContain("[link=");
    }

    [Fact]
    public void HttpFunction_WithNoRoute_FallsBackToDash()
    {
        var fn = MakeFunction(triggerType: "http", route: null, methods: ["GET"]);

        string result = HttpRouteFormatter.FormatRouteMarkup(fn, "http://localhost:7071", _theme);

        result.Should().Be("GET —");
        result.Should().NotContain("[link=");
    }

    [Fact]
    public void LinkMarkup_RendersAsOsc8_OnCapableConsole()
    {
        // Verifies the contract the CompactRenderer relies on: a
        // [link=URL]URL[/] Markup written through Spectre.Console with
        // Links capability on produces an OSC 8 hyperlink in the byte
        // stream. If Spectre ever changes this behavior we'll notice here.
        //
        // The exact sequence Spectre emits is
        //   ESC ] 8 ; id=<n> ; URL ESC \ DISPLAY ESC ] 8 ; ; ESC \
        // — i.e., the URL appears in the params block following an
        // auto-assigned id. We assert on the stable invariants only.
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Capabilities.Links = true;
        console.Profile.Width = 200;

        var fn = MakeFunction(triggerType: "http", route: "/api/hello", methods: ["GET", "POST"]);
        string markup = HttpRouteFormatter.FormatRouteMarkup(fn, "http://localhost:7071", _theme);

        console.Markup(markup);
        string output = writer.ToString();

        output.Should().Contain("\u001b]8;");
        output.Should().Contain("http://localhost:7071/api/hello");
        output.Should().Contain("\u001b]8;;\u001b\\");
    }

    private static FunctionInfo MakeFunction(string triggerType, string? route, IReadOnlyList<string> methods)
        => new(
            Name: "Test",
            TriggerType: triggerType,
            Route: route,
            HttpMethods: methods,
            Status: FunctionStatus.Ready,
            ActiveInvocations: 0,
            TotalInvocations: 0,
            TotalErrors: 0,
            LastInvocationAt: null,
            LastErrorMessage: null);
}
