// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class CompactLogComponentsTests
{
    [Fact]
    public void CompactLogBuffer_WhenCapacityExceeded_DropsOldestLines()
    {
        var buffer = new CompactLogBuffer(capacity: 2);

        buffer.Add(new CompactLogLine(new Text("first"), "First", IsError: false, LogLevel.Information));
        buffer.Add(new CompactLogLine(new Text("second"), "Second", IsError: false, LogLevel.Information));
        buffer.Add(new CompactLogLine(new Text("third"), "Third", IsError: true, LogLevel.Error));

        CompactLogLine[] lines = buffer.Snapshot();

        Assert.Equal(2, lines.Length);
        Assert.Equal("Second", lines[0].FunctionName);
        Assert.Equal("Third", lines[1].FunctionName);
        Assert.True(lines[1].IsError);
    }

    [Fact]
    public void Format_WithRawFunctionLog_CapturesFilteringMetadata()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Function.HttpTrigger1",
            LogLevel.Warning,
            default,
            "Queue depth is high",
            Exception: null,
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
            });

        CompactLogLine line = formatter.Format(entry, [], listenUri: null)!;

        Assert.Equal("HttpTrigger1", line.FunctionName);
        Assert.False(line.IsError);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("Queue depth is high", Render(line.Renderable));
    }

    [Fact]
    public void Format_WithFailedInvocation_PromotesLineToError()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Function.HttpTrigger1",
            LogLevel.Information,
            default,
            "Invocation completed",
            Exception: null,
            HostLogEntry.EmptyAttributes);
        DashboardEvent[] events =
        [
            new InvocationCompletedEvent(
                DateTimeOffset.UnixEpoch,
                "HttpTrigger1",
                "invocation-1",
                TraceId: null,
                Result: "failed",
                DurationMs: 42,
                ErrorType: "InvalidOperationException",
                ErrorMessage: "Boom"),
        ];

        CompactLogLine line = formatter.Format(entry, events, listenUri: null)!;

        Assert.Equal("HttpTrigger1", line.FunctionName);
        Assert.True(line.IsError);
        Assert.Equal(LogLevel.Error, line.Level);
        string output = Render(line.Renderable);
        Assert.Contains("InvalidOperationException", output);
        Assert.Contains("Boom", output);
    }

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
