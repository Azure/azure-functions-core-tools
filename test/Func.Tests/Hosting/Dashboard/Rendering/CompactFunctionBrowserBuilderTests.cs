// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class CompactFunctionBrowserBuilderTests
{
    [Fact]
    public void GetTotalRows_WithOddFunctionCount_RoundsUpForTwoColumns()
    {
        var builder = new CompactFunctionBrowserBuilder(new DefaultTheme(), new FunctionPalette());

        int totalRows = builder.GetTotalRows(functionCount: 5);

        Assert.Equal(3, totalRows);
    }

    [Fact]
    public void Build_WithActiveFunction_RendersActiveCount()
    {
        var builder = new CompactFunctionBrowserBuilder(new DefaultTheme(), new FunctionPalette());
        FunctionInfo[] functions =
        [
            CreateFunction("HttpTrigger", FunctionStatus.Active, activeInvocations: 3),
            CreateFunction("QueueProcessor", FunctionStatus.Ready, activeInvocations: 0),
        ];

        IRenderable renderable = builder.Build(
            functions,
            totalRows: builder.GetTotalRows(functions.Length),
            visibleRows: builder.GetVisibleRows(functions.Length, viewportHeight: 24),
            rowOffset: 0,
            selectedIndex: 0);

        string output = Render(renderable);
        Assert.Contains("Functions (2)", output);
        Assert.Contains("HttpTrigger (3)", output);
        Assert.Contains("QueueProcessor", output);
    }

    [Fact]
    public void Build_WithNoFunctions_RendersEmptyMessage()
    {
        var builder = new CompactFunctionBrowserBuilder(new DefaultTheme(), new FunctionPalette());

        IRenderable renderable = builder.Build([], totalRows: 1, visibleRows: 1, rowOffset: 0, selectedIndex: 0);

        string output = Render(renderable);
        Assert.Contains("Functions (0)", output);
        Assert.Contains("No functions loaded yet", output);
    }

    private static FunctionInfo CreateFunction(string name, FunctionStatus status, int activeInvocations)
        => new(
            name,
            "http",
            "/api/hello",
            [],
            status,
            activeInvocations,
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
