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

        buffer.Add(new CompactLogLine(new Text("first"), "First", isError: false, LogLevel.Information));
        buffer.Add(new CompactLogLine(new Text("second"), "Second", isError: false, LogLevel.Information));
        buffer.Add(new CompactLogLine(new Text("third"), "Third", isError: true, LogLevel.Error));

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
    public void Format_WithLongRawFunctionLog_WrapsIntoVisualRows()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Function.HttpTrigger1",
            LogLevel.Information,
            default,
            new string('x', 120),
            Exception: null,
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
            });

        CompactLogLine line = formatter.Format(entry, [], listenUri: null)!;

        Assert.True(line.RenderRows(width: 80).Count > 1);
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
                Error: new HostLogExceptionDetails("InvalidOperationException", "Boom", null, null)),
        ];

        CompactLogLine line = formatter.Format(entry, events, listenUri: null)!;

        Assert.Equal("HttpTrigger1", line.FunctionName);
        Assert.True(line.IsError);
        Assert.Equal(LogLevel.Error, line.Level);
        string output = Render(line.Renderable);
        Assert.Contains("InvalidOperationException", output);
        Assert.Contains("Boom", output);
    }

    [Fact]
    public void Format_WithExceptionDetails_RendersSummaryWithoutCategory()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var exceptionDetails = new HostLogExceptionDetails(
            "Microsoft.Azure.WebJobs.Script.Workers.WorkerProcessExitException",
            "Language worker process exited.",
            Stack: null,
            InnerException: new HostLogExceptionDetails("System.InvalidOperationException", "A connection string was not found.", null, null));
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Grpc",
            LogLevel.Error,
            default,
            "Language Worker Process exited.",
            Exception: null,
            HostLogEntry.EmptyAttributes)
        {
            ExceptionDetails = exceptionDetails,
        };

        CompactLogLine line = formatter.Format(entry, [], listenUri: null)!;

        string output = RenderRows(line);
        Assert.Null(line.FunctionName);
        Assert.True(line.IsError);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.DoesNotContain("Grpc", output);
        Assert.Contains("Language Worker Process exited.", output);
        Assert.Contains("Microsoft.Azure.WebJobs.Script.Workers.WorkerProcessExitException", output);
        Assert.Contains("A connection string was not found.", output);
    }

    [Fact]
    public void Format_WithExceptionDetailsOnly_RendersSummary()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Grpc",
            LogLevel.Error,
            default,
            string.Empty,
            Exception: null,
            HostLogEntry.EmptyAttributes)
        {
            ExceptionDetails = new HostLogExceptionDetails("System.InvalidOperationException", "A connection string was not found.", null, null),
        };

        CompactLogLine? line = formatter.Format(entry, [], listenUri: null);

        Assert.NotNull(line);
        Assert.Contains("A connection string was not found.", RenderRows(line));
    }

    [Fact]
    public void Format_WithEmptyLogMessage_SuppressesLine()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService",
            LogLevel.Information,
            new EventId(0),
            string.Empty,
            Exception: null,
            HostLogEntry.EmptyAttributes);

        CompactLogLine? line = formatter.Format(entry, [], listenUri: null);

        Assert.Null(line);
    }

    [Fact]
    public void Format_WithOptionsLoggingService_SuppressesLine()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService",
            LogLevel.Information,
            new EventId(0),
            "ConcurrencyOptions",
            Exception: null,
            HostLogEntry.EmptyAttributes);

        CompactLogLine? line = formatter.Format(entry, [], listenUri: null);

        Assert.Null(line);
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
        console.Profile.Width = 240;

        console.Write(renderable);
        return writer.ToString();
    }

    private static string RenderRows(CompactLogLine line)
        => string.Join(Environment.NewLine, line.RenderRows(width: 240).Select(Render));
}
