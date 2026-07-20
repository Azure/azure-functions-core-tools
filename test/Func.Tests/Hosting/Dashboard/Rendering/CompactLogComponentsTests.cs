// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

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

        lines.Length.Should().Be(2);
        lines[0].FunctionName.Should().Be("Second");
        lines[1].FunctionName.Should().Be("Third");
        lines[1].IsError.Should().BeTrue();
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

        line.FunctionName.Should().Be("HttpTrigger1");
        line.IsError.Should().BeFalse();
        line.Level.Should().Be(LogLevel.Warning);
        Render(line.Renderable).Should().Contain("Queue depth is high");
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

        (line.RenderRows(width: 80).Count > 1).Should().BeTrue();
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

        line.FunctionName.Should().Be("HttpTrigger1");
        line.IsError.Should().BeTrue();
        line.Level.Should().Be(LogLevel.Error);
        string output = Render(line.Renderable);
        output.Should().Contain("InvalidOperationException");
        output.Should().Contain("Boom");
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
        line.FunctionName.Should().BeNull();
        line.IsError.Should().BeTrue();
        line.Level.Should().Be(LogLevel.Error);
        output.Should().NotContain("Grpc");
        output.Should().Contain("Language Worker Process exited.");
        output.Should().Contain("Microsoft.Azure.WebJobs.Script.Workers.WorkerProcessExitException");
        output.Should().Contain("A connection string was not found.");
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

        line.Should().NotBeNull();
        RenderRows(line).Should().Contain("A connection string was not found.");
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

        line.Should().BeNull();
    }

    [Fact]
    public void Format_WithInitializationStepLog_LabelsSourceAsInitialization()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "[startup]",
            LogLevel.Information,
            default,
            "Resolving host workload",
            Exception: null,
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.StartInitializationStepCompleted,
            });

        CompactLogLine line = formatter.Format(entry, [], listenUri: null)!;

        string output = RenderRows(line);
        line.FunctionName.Should().BeNull();
        output.Should().Contain("Initialization");
        output.Should().Contain("Resolving host workload");
    }

    [Fact]
    public void Format_WithPlainHostLog_DoesNotLabelInitialization()
    {
        var formatter = new CompactLogLineFormatter(new DefaultTheme(), new FunctionPalette());
        var entry = new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Host.Startup",
            LogLevel.Information,
            default,
            "Reading host configuration",
            Exception: null,
            HostLogEntry.EmptyAttributes);

        CompactLogLine line = formatter.Format(entry, [], listenUri: null)!;

        string output = RenderRows(line);
        line.FunctionName.Should().BeNull();
        output.Should().NotContain("Initialization");
        output.Should().Contain("Reading host configuration");
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
