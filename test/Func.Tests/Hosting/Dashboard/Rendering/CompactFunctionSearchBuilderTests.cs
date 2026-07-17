// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class CompactFunctionSearchBuilderTests
{
    [Fact]
    public void GetMatches_WithEmptyQuery_ReturnsAllFunctions()
    {
        var builder = new CompactFunctionSearchBuilder(new DefaultTheme(), new FunctionPalette());
        FunctionInfo[] functions =
        [
            CreateFunction("HttpTrigger", "http", "/api/hello"),
            CreateFunction("QueueProcessor", "queue", "orders"),
        ];

        FunctionInfo[] matches = builder.GetMatches(functions, " ");

        matches.Should().BeSameAs(functions);
    }

    [Fact]
    public void GetMatches_WithQuery_MatchesNameTriggerAndRoute()
    {
        var builder = new CompactFunctionSearchBuilder(new DefaultTheme(), new FunctionPalette());
        FunctionInfo[] functions =
        [
            CreateFunction("HttpTrigger", "http", "/api/hello"),
            CreateFunction("QueueProcessor", "queue", "orders"),
            CreateFunction("TimerCleanup", "timer", null),
        ];

        FunctionInfo[] matches = builder.GetMatches(functions, "ord");

        matches.Should().ContainSingle();
        matches[0].Name.Should().Be("QueueProcessor");
    }

    [Fact]
    public void Build_WithNoMatches_RendersNoMatchesMessage()
    {
        var builder = new CompactFunctionSearchBuilder(new DefaultTheme(), new FunctionPalette());

        IRenderable renderable = builder.Build("missing", [], visibleRows: 3, rowOffset: 0, selectedIndex: 0);

        string output = Render(renderable);
        output.Should().Contain("Search functions");
        output.Should().Contain("No functions match \"missing\"");
    }

    private static FunctionInfo CreateFunction(string name, string triggerType, string? route)
        => new(
            name,
            triggerType,
            route,
            [],
            FunctionStatus.Ready,
            ActiveInvocations: 0,
            TotalInvocations: 0,
            TotalErrors: 0,
            LastInvocationAt: null,
            LastErrorMessage: null);

    private static string Render(IRenderable renderable)
    {
        using var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });

        console.Write(renderable);
        return writer.ToString();
    }
}
