// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard;

public class DashboardStateTests
{
    [Fact]
    public void Observe_HostStateAttribute_EmitsHostStateChangedEvent()
    {
        var state = new DashboardState();
        var entry = MakeEntry(
            "Host.Lifecycle",
            LogLevel.Information,
            "Host ready",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "ready",
                [HostLogAttributeKeys.HostStartupDurationMs] = 1241.0,
            });

        IReadOnlyList<DashboardEvent> events = state.Observe(entry);

        var ev = events.OfType<HostStateChangedEvent>().Should().ContainSingle().Subject;
        ev.From.Should().Be(HostLifecycleState.Starting);
        ev.To.Should().Be(HostLifecycleState.Ready);
        ev.DurationMs.Should().Be(1241.0);
    }

    [Fact]
    public void Observe_FunctionDiscovered_PopulatesSnapshot()
    {
        var state = new DashboardState();
        var entry = MakeEntry(
            "Host.Indexer",
            LogLevel.Information,
            "Function loaded",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionTriggerType] = "http",
                [HostLogAttributeKeys.FunctionRoute] = "/api/hello",
                [HostLogAttributeKeys.FunctionHttpMethods] = new[] { "GET", "POST" },
            });

        IReadOnlyList<DashboardEvent> events = state.Observe(entry);

        events.OfType<FunctionDiscoveredEvent>().Should().ContainSingle();
        DashboardSnapshot snap = state.Snapshot();
        var fn = snap.Functions.Should().ContainSingle().Subject;
        fn.Name.Should().Be("HttpTrigger1");
        fn.TriggerType.Should().Be("http");
        fn.HttpMethods.Should().Equal(["GET", "POST"]);
    }

    [Fact]
    public void Observe_InvocationLifecycle_UpdatesCounters()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));

        state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Information,
            "Executing 'HttpTrigger1'",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationStarted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-1",
            }));

        state.Snapshot().Functions[0].Status.Should().Be(FunctionStatus.Active);
        state.Snapshot().ActiveInvocationCount.Should().Be(1);

        IReadOnlyList<DashboardEvent> events = state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Information,
            "Executed 'HttpTrigger1'",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-1",
                [HostLogAttributeKeys.FunctionResult] = "succeeded",
                [HostLogAttributeKeys.DurationMs] = 12.0,
            }));

        events.OfType<InvocationCompletedEvent>().Should().ContainSingle();
        DashboardSnapshot snap = state.Snapshot();
        snap.TotalInvocations.Should().Be(1);
        snap.SucceededInvocations.Should().Be(1);
        snap.FailedInvocations.Should().Be(0);
        snap.ActiveInvocationCount.Should().Be(0);
        snap.Functions[0].Status.Should().Be(FunctionStatus.Ready);
    }

    [Fact]
    public void Observe_FailedInvocation_FlagsFunctionAsError()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));
        var exceptionDetails = new HostLogExceptionDetails(
            "Microsoft.Azure.WebJobs.Host.FunctionInvocationException",
            "Exception while executing function",
            "outer stack",
            new HostLogExceptionDetails("Worker.UserException", "inner boom", "inner stack", null));

        IReadOnlyList<DashboardEvent> events = state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Error,
            "Executed 'HttpTrigger1' (Failed)",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-2",
                [HostLogAttributeKeys.FunctionResult] = "failed",
                [HostLogAttributeKeys.DurationMs] = 38.0,
            },
            exception: new InvalidOperationException("placeholder"),
            exceptionDetails: exceptionDetails));

        var completed = events.OfType<InvocationCompletedEvent>().Should().ContainSingle().Subject;
        completed.ErrorType.Should().Be("Microsoft.Azure.WebJobs.Host.FunctionInvocationException");
        completed.ErrorMessage.Should().Be("Exception while executing function");
        (completed.Error?.InnerException?.Type).Should().Be("Worker.UserException");
        (completed.Error?.InnerException?.Message).Should().Be("inner boom");

        DashboardSnapshot snap = state.Snapshot();
        snap.Functions[0].Status.Should().Be(FunctionStatus.Error);
        snap.Functions[0].TotalErrors.Should().Be(1);
        snap.FailedInvocations.Should().Be(1);
        const string ExpectedLastErrorMessage =
            "Microsoft.Azure.WebJobs.Host.FunctionInvocationException: Exception while executing function" +
            " ---> Worker.UserException: inner boom";
        snap.Functions[0].LastErrorMessage.Should().Be(ExpectedLastErrorMessage);
        (snap.ErrorCount > 0).Should().BeTrue();
    }

    [Fact]
    public void Observe_HostRecycling_ClearsFunctionsOnReady()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));
        state.Observe(MakeEntry(
            "Host.Lifecycle",
            LogLevel.Information,
            "Recycling",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "recycling",
            }));

        state.Snapshot().Functions.Should().ContainSingle(); // not yet cleared

        state.Observe(MakeEntry(
            "Host.Lifecycle",
            LogLevel.Information,
            "Host ready",
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "ready",
            }));

        state.Snapshot().Functions.Should().BeEmpty();
    }

    [Fact]
    public void BuildSummary_ReportsCounters()
    {
        var state = new DashboardState();
        state.Observe(DiscoverHttp("HttpTrigger1"));
        state.Observe(MakeEntry(
            "Function.HttpTrigger1",
            LogLevel.Information,
            string.Empty,
            new()
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-x",
                [HostLogAttributeKeys.FunctionResult] = "succeeded",
                [HostLogAttributeKeys.DurationMs] = 5.0,
            }));

        SummaryEvent summary = state.BuildSummary("sigint", DateTimeOffset.UtcNow);

        summary.ExitReason.Should().Be("sigint");
        summary.FunctionCount.Should().Be(1);
        summary.TotalInvocations.Should().Be(1);
        summary.SucceededInvocations.Should().Be(1);
    }

    private static HostLogEntry DiscoverHttp(string name) => MakeEntry(
        "Host.Indexer",
        LogLevel.Information,
        "Loaded",
        new()
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
            [HostLogAttributeKeys.FunctionName] = name,
            [HostLogAttributeKeys.FunctionTriggerType] = "http",
        });

    private static HostLogEntry MakeEntry(
        string category,
        LogLevel level,
        string message,
        Dictionary<string, object?> attributes,
        Exception? exception = null,
        HostLogExceptionDetails? exceptionDetails = null)
    {
        var entry = new HostLogEntry(DateTimeOffset.UtcNow, category, level, default, message, exception, attributes);
        return exceptionDetails is not null ? entry with { ExceptionDetails = exceptionDetails } : entry;
    }
}
